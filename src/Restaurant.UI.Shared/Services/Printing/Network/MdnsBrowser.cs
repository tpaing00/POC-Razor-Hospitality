using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Restaurant.UI.Shared.Services.Printing.Network;

/// <summary>
/// One multicast question, and whatever answers inside a fixed window.
/// <see cref="MdnsMessage"/> is the bytes; this is the socket.
///
/// **The service types asked for, and why these three.** A Star TSP143IV with the
/// Ethernet and Wi-Fi interface advertises itself the way every network printer built
/// this decade does:
/// <list type="bullet">
/// <item><c>_pdl-datastream._tcp</c> — raw printing, and the one that carries port
/// 9100 in its SRV. This is the answer that is directly usable.</item>
/// <item><c>_printer._tcp</c> — LPR on 515. Asked for because a unit that advertises
/// it and not the first still tells us its address, and 9100 is open on a Star unit
/// whether or not it says so.</item>
/// <item><c>_ipp._tcp</c> — IPP on 631, same reasoning.</item>
/// </list>
/// The port in the SRV is used when it is 9100 and replaced with 9100 when it is not,
/// because this build speaks Star Line over a raw socket and not IPP. That
/// substitution is the one guess in the whole discovery path and it is named in the
/// device's own address so a person can see what will be dialled.
///
/// **The window is fixed and short.** mDNS has no "that is all" — responders answer
/// when they feel like it, and some stagger replies by up to a second to avoid
/// colliding. Two and a half seconds is long enough for a printer on the same switch
/// and short enough that a person tapping SCAN does not think the screen has stopped.
/// Waiting longer would find nothing a second query would not.
/// </summary>
[UnsupportedOSPlatform("browser")]
internal static class MdnsBrowser
{
    /// <summary>The service types asked for, in one packet.</summary>
    public static readonly string[] PrinterServices =
    {
        "_pdl-datastream._tcp.local",
        "_printer._tcp.local",
        "_ipp._tcp.local"
    };

    /// <summary>How long to listen. See the note above on why it is not longer.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(2500);

    /// <summary>
    /// Ask, listen, and return what answered.
    ///
    /// Never throws. A host that will not open a multicast socket — a container with no
    /// multicast route, a firewall rule, a locked-down mobile network — contributes an
    /// empty list, and the subnet probe and the manual address field are what follow.
    /// </summary>
    public static async Task<IReadOnlyList<MdnsPrinter>> BrowseAsync(CancellationToken cancellationToken = default)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var unicast = false;

        try
        {
            // Sharing port 5353 rather than taking it. A Windows host with Bonjour
            // installed, or a Linux host running avahi, already holds that port; binding
            // exclusively would fail on exactly the machines most likely to have a
            // printer on them. When the share is refused anyway, an ephemeral port with
            // the unicast-response bit set is the fallback — fewer responders honour it,
            // which is why it is second.
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, MdnsMessage.MulticastPort));
        }
        catch (Exception)
        {
            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                unicast = true;
            }
            catch (Exception)
            {
                return Array.Empty<MdnsPrinter>();
            }
        }

        var interfaces = MulticastInterfaces();
        var joined = false;

        foreach (var index in interfaces)
        {
            try
            {
                socket.SetSocketOption(
                    SocketOptionLevel.IP,
                    SocketOptionName.AddMembership,
                    new MulticastOption(MdnsMessage.MulticastAddress, index));
                joined = true;
            }
            catch (Exception)
            {
                // An interface that will not join the group is one this cannot see
                // printers on. The others still can.
            }
        }

        if (!joined && !unicast)
        {
            // Nothing joined and the socket is on the multicast port, so nothing will
            // arrive. Say nothing found rather than waiting out the window.
            return Array.Empty<MdnsPrinter>();
        }

        var query = MdnsMessage.BuildQuery(PrinterServices, unicast);
        var destination = new IPEndPoint(MdnsMessage.MulticastAddress, MdnsMessage.MulticastPort);

        // Once per interface. A back office on a machine with a wired network and a
        // wireless one has to ask on both: the printer is on whichever the venue
        // cabled, and the host has no way to know which.
        foreach (var index in interfaces)
        {
            try
            {
                socket.SetSocketOption(
                    SocketOptionLevel.IP,
                    SocketOptionName.MulticastInterface,
                    IPAddress.HostToNetworkOrder(index));
                await socket.SendToAsync(query, SocketFlags.None, destination, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        if (interfaces.Count == 0)
        {
            try
            {
                await socket.SendToAsync(query, SocketFlags.None, destination, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                return Array.Empty<MdnsPrinter>();
            }
        }

        var pointers = new List<MdnsPointer>();
        var services = new Dictionary<string, MdnsService>(StringComparer.OrdinalIgnoreCase);
        var addresses = new Dictionary<string, IPAddress>(StringComparer.OrdinalIgnoreCase);

        using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        window.CancelAfter(Window);

        var buffer = new byte[9000];
        var from = new IPEndPoint(IPAddress.Any, 0);

        while (!window.IsCancellationRequested)
        {
            try
            {
                var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, from, window.Token)
                    .ConfigureAwait(false);

                if (result.ReceivedBytes <= 0)
                {
                    continue;
                }

                var records = MdnsMessage.Parse(buffer.AsSpan(0, result.ReceivedBytes).ToArray());
                pointers.AddRange(records.Pointers);

                // Later answers win. A responder correcting itself inside one window is
                // rare; a responder repeating itself is not, and the last word costs
                // nothing.
                foreach (var pair in records.Services)
                {
                    services[pair.Key] = pair.Value;
                }

                foreach (var pair in records.Addresses)
                {
                    addresses[pair.Key] = pair.Value;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                break;
            }
        }

        var found = MdnsMessage.Printers(new MdnsRecords(pointers, services, addresses));

        // Only the ones that are actually reachable as a raw socket. A printer that
        // advertised IPP and nothing else is still dialled on 9100, because that is the
        // port a Star unit answers Star Line on whatever else it advertises.
        return found
            .Select(p => p.Port == SubnetProbe.RawPrintingPort
                ? p
                : p with { Port = SubnetProbe.RawPrintingPort })
            .ToList();
    }

    /// <summary>
    /// The interface indexes worth asking on: up, IPv4, multicast-capable, not
    /// loopback and not a tunnel.
    /// </summary>
    private static IReadOnlyList<int> MulticastInterfaces()
    {
        var indexes = new List<int>();

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    !nic.SupportsMulticast ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var properties = nic.GetIPProperties();
                var v4 = properties.GetIPv4Properties();
                if (v4 is not null)
                {
                    indexes.Add(v4.Index);
                }
            }
        }
        catch (Exception)
        {
        }

        return indexes;
    }
}
