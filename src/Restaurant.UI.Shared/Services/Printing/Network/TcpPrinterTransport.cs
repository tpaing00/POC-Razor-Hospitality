using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Restaurant.UI.Shared.Services.Printing.Network;

/// <summary>
/// <see cref="IPrinterTransport"/> over a raw TCP socket — the Ethernet and Wi-Fi half
/// of the TSP143IV-UEWB, and the transport the back office runs on.
///
/// **It is the second implementation of the seam and nothing above it moved.**
/// <see cref="BagTicket"/>, <see cref="StarLine"/>, <see cref="StarLineStatus"/>,
/// <see cref="TransportReceiptPrinter"/> and the setup screen are all unchanged: a
/// socket is a socket whether the far end is a radio or an address, and Star Line over
/// port 9100 is byte for byte what goes down an RFCOMM stream. There is no framing to
/// add — port 9100 is raw print data by definition, which is exactly why Star exposes
/// it and why a print job needs no protocol on top.
///
/// **Discovery is three things in a deliberate order**, because a restaurant network
/// is not a lab:
/// <list type="number">
/// <item><b>mDNS.</b> One multicast frame, a two and a half second window, and the
/// answers come back with the printer's own name attached. This is the path that works
/// and it costs the network almost nothing.</item>
/// <item><b>A bounded subnet probe</b>, and only when the first found nothing. It is
/// capped at a /24, at 254 addresses, at 24 connections in flight and at 400ms a host,
/// and it produces an address with no name because that is all a TCP accept proves. It
/// exists because multicast is routinely dropped — client isolation, two VLANs, IGMP
/// snooping with no querier — and a design with no answer for that strands a venue.</item>
/// <item><b>An address somebody typed</b>, always available and never overwritten by a
/// scan. Every network printer prints its own address on its self-test page, and that
/// page works when nothing else does.</item>
/// </list>
///
/// **What it will not do: pretend.** A host that accepts a connection on 9100 is
/// listed as an address and named as one. It is not called a Star TSP143IV, it is not
/// drawn ready, and the test label is the only thing that turns it into a printer that
/// works — the same rule the Bluetooth path already runs on.
/// </summary>
[UnsupportedOSPlatform("browser")]
public sealed class TcpPrinterTransport : IAddressablePrinterTransport
{
    /// <summary>Star's raw printing port. The default when an address carries no port.</summary>
    public const int DefaultPort = SubnetProbe.RawPrintingPort;

    /// <summary>
    /// How long a connect gets before it is called unreachable. Short, because the
    /// caller is a person watching a screen and a printer on the same subnet answers in
    /// milliseconds; a printer that needs longer than this is one somebody has to go
    /// and look at.
    /// </summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Addresses a person typed, kept for the life of the process so a scan does not
    /// wipe them off the list. The remembered selection is
    /// <see cref="IPrinterPreference"/>'s job and is separate; this is only so that
    /// tapping SCAN after typing an address does not make the address disappear.
    /// </summary>
    private readonly List<PrinterDevice> _typed = new();

    public string Name => "Network";

    /// <summary>
    /// Available whenever the host has a network interface that is up and not
    /// loopback.
    ///
    /// This is deliberately not a reachability test. Whether the printer answers is
    /// what the test label is for; whether this host has a network at all is what this
    /// question means, and a host with no network says so in a sentence rather than
    /// producing an empty list a person would read as "no printers in the building".
    /// </summary>
    public bool IsAvailable(out string? reason)
    {
        // Cached for a moment, because the screen reads this several times per render
        // and enumerating every network interface is real work for an answer that
        // cannot have changed between two frames of the same second. Short enough that
        // unplugging a cable shows up on the next scan.
        lock (_availabilityLock)
        {
            if (DateTime.UtcNow - _availabilityAt < AvailabilityLife)
            {
                reason = _availabilityReason;
                return _availabilityReason is null;
            }

            try
            {
                _availabilityReason = SubnetProbe.LocalSubnets().Count == 0
                    ? "This host is not on a network · connect it to the venue network, then scan"
                    : null;
            }
            catch (Exception ex)
            {
                _availabilityReason = $"Could not read this host's network · {ex.Message}";
            }

            _availabilityAt = DateTime.UtcNow;
            reason = _availabilityReason;
            return _availabilityReason is null;
        }
    }

    private static readonly TimeSpan AvailabilityLife = TimeSpan.FromSeconds(5);
    private readonly object _availabilityLock = new();
    private DateTime _availabilityAt = DateTime.MinValue;
    private string? _availabilityReason;

    /// <summary>
    /// mDNS, then a bounded probe only if that found nothing, then whatever was typed.
    /// </summary>
    public async Task<IReadOnlyList<PrinterDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var found = new Dictionary<string, PrinterDevice>(StringComparer.OrdinalIgnoreCase);

        foreach (var printer in await MdnsBrowser.BrowseAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = $"{printer.Host}:{printer.Port}";
            found[id] = new PrinterDevice(id, printer.Name, IsPaired: true);
        }

        if (found.Count == 0)
        {
            foreach (var address in await ProbeAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = $"{address}:{DefaultPort}";

                // Named for what is known about it, which is an address. Calling it a
                // printer would be the one invention this feature does not make: 9100
                // is a port, not a promise.
                found[id] = new PrinterDevice(id, $"Device at {address}", IsPaired: false);
            }
        }

        lock (_typed)
        {
            foreach (var device in _typed)
            {
                found[device.Id] = device;
            }
        }

        return found.Values.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static async Task<IReadOnlyList<IPAddress>> ProbeAsync(CancellationToken cancellationToken)
    {
        var candidates = new List<IPAddress>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (local, prefix) in SubnetProbe.LocalSubnets())
        {
            foreach (var address in SubnetProbe.Candidates(local, prefix))
            {
                if (seen.Add(address.ToString()))
                {
                    candidates.Add(address);
                }
            }
        }

        return candidates.Count == 0
            ? Array.Empty<IPAddress>()
            : await SubnetProbe.ScanAsync(candidates, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Validate what somebody typed and record it. Deliberately does not connect: see
    /// <see cref="IAddressablePrinterTransport.ResolveAsync"/> for why a printer that is
    /// switched off still has an address worth keeping.
    /// </summary>
    public Task<PrinterDevice> ResolveAsync(string address, CancellationToken cancellationToken = default)
    {
        var (host, port) = ParseAddress(address);
        var id = $"{host}:{port}";

        // Named for what is known about it, which is an address — and named
        // differently from the address itself, so the row is not the same string
        // twice. Typing an address says where to send bytes; it does not say a printer
        // is there, and only the test label does.
        var device = new PrinterDevice(id, $"Device at {host}", IsPaired: false);

        lock (_typed)
        {
            _typed.RemoveAll(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
            _typed.Add(device);
        }

        return Task.FromResult(device);
    }

    /// <summary>
    /// <c>192.168.1.50</c>, <c>192.168.1.50:9100</c>, <c>printer.local</c> and
    /// <c>[fe80::1]:9100</c> all parse. Anything else throws with a sentence saying
    /// what was expected, because "invalid input" tells a person nothing they can act
    /// on.
    /// </summary>
    internal static (string Host, int Port) ParseAddress(string address)
    {
        var text = (address ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            throw new FormatException(
                "Type the printer's address · it is on the printer's self-test page, like 192.168.1.50");
        }

        // A scheme is a thing people paste out of a browser after finding the printer's
        // own web page. Stripping it is kinder than refusing it.
        foreach (var scheme in new[] { "http://", "https://", "tcp://", "socket://" })
        {
            if (text.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                text = text[scheme.Length..];
            }
        }

        text = text.TrimEnd('/');

        var port = DefaultPort;
        string host;

        if (text.StartsWith('['))
        {
            // A bracketed IPv6 literal, with or without a port.
            var close = text.IndexOf(']');
            if (close < 0)
            {
                throw new FormatException("That address is missing its closing bracket · like [fe80::1]:9100");
            }

            host = text[1..close];
            var rest = text[(close + 1)..];
            if (rest.StartsWith(':'))
            {
                port = ParsePort(rest[1..]);
            }
        }
        else
        {
            var colon = text.LastIndexOf(':');

            // An unbracketed IPv6 literal has several colons and no port. One colon is
            // a port; more than one is an address.
            if (colon > 0 && text.IndexOf(':') == colon)
            {
                host = text[..colon];
                port = ParsePort(text[(colon + 1)..]);
            }
            else
            {
                host = text;
            }
        }

        host = host.Trim();

        if (host.Length == 0)
        {
            throw new FormatException(
                "Type the printer's address · it is on the printer's self-test page, like 192.168.1.50");
        }

        // An address is either an IP literal or a host name. A host name is letters,
        // digits, dots and hyphens; anything else is a typing mistake worth catching
        // here rather than as a socket error four seconds later.
        if (!IPAddress.TryParse(host, out _) &&
            !host.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
        {
            throw new FormatException(
                $"“{host}” is not an address or a host name · try 192.168.1.50 or 192.168.1.50:9100");
        }

        return (host, port);
    }

    private static int ParsePort(string text)
    {
        if (!int.TryParse(text, out var port) || port is < 1 or > 65535)
        {
            throw new FormatException(
                $"“{text}” is not a port · leave it off to use {DefaultPort}, which is what Star printers listen on");
        }

        return port;
    }

    public async Task<IPrinterConnection> ConnectAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var (host, port) = ParseAddress(deviceId);

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            // Nagle would hold the last partial frame of a ticket waiting for more to
            // come, which on a job that ends with a cut command is the cut arriving
            // late. A print job is a burst and then silence; that is the case Nagle is
            // worst at.
            NoDelay = true
        };

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);

            try
            {
                await socket.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"nothing answered at {host}:{port} within {ConnectTimeout.TotalSeconds:0} seconds");
            }
        }
        catch (Exception)
        {
            socket.Dispose();
            throw;
        }

        return new TcpPrinterConnection(socket);
    }

    /// <summary>
    /// One open socket. Written to and closed, exactly like the RFCOMM one: a printer
    /// on 9100 accepts one connection at a time, and a socket held open across a shift
    /// is a printer no other terminal can reach.
    /// </summary>
    private sealed class TcpPrinterConnection(Socket socket) : IPrinterConnection
    {
        public async Task WriteAsync(byte[] payload, CancellationToken cancellationToken = default)
        {
            var sent = 0;
            while (sent < payload.Length)
            {
                // A socket send is allowed to take part of the buffer. A ticket that
                // arrives with its middle missing is a label somebody throws away, so
                // the loop is not optional.
                var count = await socket
                    .SendAsync(payload.AsMemory(sent), SocketFlags.None, cancellationToken)
                    .ConfigureAwait(false);

                if (count <= 0)
                {
                    throw new IOException("the printer closed the connection while the ticket was being sent");
                }

                sent += count;
            }
        }

        /// <summary>
        /// Listen for an Automatic Status Back block for as long as the caller allows,
        /// and give up quietly. Hearing nothing is a normal outcome the caller records
        /// as an unread status rather than as health — the same contract the Bluetooth
        /// connection honours, because <see cref="TransportReceiptPrinter"/> cannot tell
        /// the two apart and must not have to.
        /// </summary>
        public async Task<byte[]> ReadAsync(TimeSpan wait, CancellationToken cancellationToken = default)
        {
            var buffer = new byte[64];

            using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            window.CancelAfter(wait);

            try
            {
                var count = await socket.ReceiveAsync(buffer, SocketFlags.None, window.Token)
                    .ConfigureAwait(false);
                return count <= 0 ? Array.Empty<byte>() : buffer[..count];
            }
            catch (Exception)
            {
                return Array.Empty<byte>();
            }
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                // Shutdown before close, so the bytes already handed to the stack are
                // sent rather than discarded. A close on a socket with a ticket still in
                // its send buffer is a job that never prints.
                socket.Shutdown(SocketShutdown.Both);
            }
            catch (Exception)
            {
            }

            socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
