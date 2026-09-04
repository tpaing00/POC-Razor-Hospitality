using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;

namespace Restaurant.Shared.Models;

/// <summary>
/// Turns what somebody typed into the address a <see cref="Printer"/> row stores, or
/// says what is wrong with it in one sentence.
///
/// **Why the API validates at all, when the transport is the thing that dials.** The
/// registry is a record other machines read. A row holding <c>my printer</c> is not a
/// printer that is switched off, it is a row nothing can ever connect to, and it would
/// sit in the venue's list looking exactly like a working one until somebody fired a
/// test label at it. So a write is refused at the door with a sentence naming the fault
/// and an example.
///
/// **It validates shape and never reachability.** A printer switched off at half past
/// four has the same address at five, and refusing to record it because nothing
/// answered would be the server deciding a fault it has not diagnosed — the same rule
/// <c>IAddressablePrinterTransport.ResolveAsync</c> already runs on. Nothing here opens
/// a socket.
///
/// **Normalizing is what makes the unique index mean anything.** Two people typing
/// <c>192.168.1.50</c> and <c>192.168.1.50:9100</c> mean one printer, and a registry
/// that stored both strings would hold two rows for one box and let somebody edit the
/// stale one. Every accepted address comes back in one canonical form: host lower-cased
/// with an explicit port over the network, upper-case colon-separated over Bluetooth.
///
/// This is a deliberately separate, narrower parse from
/// <c>Restaurant.UI.Shared</c>'s <c>NetworkDiscovery</c>, which is the authority at
/// connect time and stays untouched: this assembly has no reference to that one, and
/// reworking a verified parser to share it would put a back-office write path inside
/// the code that talks to the printer.
/// </summary>
public static class PrinterAddress
{
    /// <summary>
    /// Star's raw printing port, and the default when an address carries none. The
    /// same 9100 <c>TcpPrinterTransport.DefaultPort</c> uses; the two constants are
    /// separate because the assemblies are, and they have to agree.
    /// </summary>
    public const int DefaultRawPrintingPort = 9100;

    /// <summary>The longest address the column takes. An IPv6 literal in brackets with
    /// a port is 47 characters, so 100 is well clear of anything real and short enough
    /// that a paste of a whole web page is refused rather than stored.</summary>
    public const int MaxLength = 100;

    /// <summary>The longest name the column takes.</summary>
    public const int MaxNameLength = 60;

    /// <summary>
    /// Normalize <paramref name="input"/> for <paramref name="transport"/>.
    /// </summary>
    /// <returns>True with <paramref name="normalized"/> set, or false with
    /// <paramref name="error"/> carrying one line naming the fault and an example
    /// (§10).</returns>
    public static bool TryNormalize(
        PrinterTransportKind transport,
        string? input,
        [NotNullWhen(true)] out string? normalized,
        [NotNullWhen(false)] out string? error)
    {
        normalized = null;
        error = null;

        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            error = transport == PrinterTransportKind.Bluetooth
                ? "An address is required · a Bluetooth printer's address looks like 00:11:62:4C:58:5D"
                : "An address is required · a network printer's address looks like 192.168.1.50 or 192.168.1.50:9100";
            return false;
        }

        if (text.Length > MaxLength)
        {
            error = $"That address is longer than {MaxLength} characters · check you pasted an address rather than a page";
            return false;
        }

        return transport switch
        {
            PrinterTransportKind.Bluetooth => TryBluetooth(text, out normalized, out error),
            PrinterTransportKind.Network => TryNetwork(text, out normalized, out error),
            _ => Fail($"'{transport}' is not a transport this build can reach · use Network or Bluetooth", out error)
        };
    }

    /// <summary>
    /// Six hex pairs. Colons, hyphens or nothing between them are all accepted because
    /// all three are printed on real hardware and read off real screens; what comes back
    /// is always the colon form in upper case, which is what the Windows and Android
    /// stacks both show.
    /// </summary>
    private static bool TryBluetooth(string text, out string? normalized, out string? error)
    {
        normalized = null;
        error = null;

        var digits = new List<char>(12);
        foreach (var c in text)
        {
            if (c is ':' or '-' or '.' or ' ')
            {
                continue;
            }

            if (!Uri.IsHexDigit(c))
            {
                return Fail(
                    $"'{text}' is not a Bluetooth address · it is six pairs of hex digits, like 00:11:62:4C:58:5D",
                    out error);
            }

            digits.Add(char.ToUpperInvariant(c));
        }

        if (digits.Count != 12)
        {
            return Fail(
                $"'{text}' is not a Bluetooth address · it is six pairs of hex digits, like 00:11:62:4C:58:5D",
                out error);
        }

        var pairs = new string[6];
        for (var i = 0; i < 6; i++)
        {
            pairs[i] = new string(new[] { digits[i * 2], digits[(i * 2) + 1] });
        }

        normalized = string.Join(':', pairs);
        return true;
    }

    /// <summary>
    /// A host and an optional port. Accepts an IPv4 literal, a bracketed IPv6 literal,
    /// a host name, and a pasted <c>http://host/</c> — that last one because a printer's
    /// own configuration page is the most likely place somebody copies its address from.
    /// </summary>
    private static bool TryNetwork(string text, out string? normalized, out string? error)
    {
        normalized = null;
        error = null;

        var candidate = StripUrl(text);

        string host;
        var port = DefaultRawPrintingPort;

        if (candidate.StartsWith('['))
        {
            // [fe80::1] or [fe80::1]:9100. The brackets exist precisely because an IPv6
            // literal is full of the character a port would otherwise be separated by.
            var close = candidate.IndexOf(']');
            if (close < 2)
            {
                return Fail(
                    $"'{text}' is not an address or a host name · try 192.168.1.50 or 192.168.1.50:9100",
                    out error);
            }

            host = candidate[1..close];
            var rest = candidate[(close + 1)..];
            if (rest.Length > 0 && !TryPort(rest, text, ref port, out error))
            {
                return false;
            }

            if (!IPAddress.TryParse(host, out var v6))
            {
                return Fail(
                    $"'{text}' is not an address or a host name · try 192.168.1.50 or 192.168.1.50:9100",
                    out error);
            }

            normalized = $"[{v6}]:{port.ToString(CultureInfo.InvariantCulture)}";
            return true;
        }

        // A bare IPv6 literal carries several colons and no brackets, so it cannot be
        // split on the last one. Recognize it before trying to.
        if (candidate.Count(c => c == ':') > 1)
        {
            if (IPAddress.TryParse(candidate, out var bare))
            {
                normalized = $"[{bare}]:{DefaultRawPrintingPort.ToString(CultureInfo.InvariantCulture)}";
                return true;
            }

            return Fail(
                $"'{text}' is not an address or a host name · an IPv6 address takes brackets, like [fe80::1]:9100",
                out error);
        }

        var cut = candidate.IndexOf(':');
        if (cut >= 0)
        {
            host = candidate[..cut];
            if (!TryPort(candidate[cut..], text, ref port, out error))
            {
                return false;
            }
        }
        else
        {
            host = candidate;
        }

        if (host.Length == 0)
        {
            return Fail(
                $"'{text}' is not an address or a host name · try 192.168.1.50 or 192.168.1.50:9100",
                out error);
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            normalized = $"{ip}:{port.ToString(CultureInfo.InvariantCulture)}";
            return true;
        }

        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            return Fail(
                $"'{text}' is not an address or a host name · try 192.168.1.50 or 192.168.1.50:9100",
                out error);
        }

        // Host names are case-insensitive, and the unique index is not. Lower-casing
        // here is what stops Printer.local and printer.local being two rows for one box.
        normalized = $"{host.ToLowerInvariant()}:{port.ToString(CultureInfo.InvariantCulture)}";
        return true;
    }

    /// <summary><paramref name="rest"/> is the colon and everything after it.</summary>
    private static bool TryPort(string rest, string original, ref int port, out string? error)
    {
        error = null;

        if (rest.Length < 2 || rest[0] != ':')
        {
            return Fail(
                $"'{original}' is not an address or a host name · try 192.168.1.50 or 192.168.1.50:9100",
                out error);
        }

        if (!int.TryParse(rest[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed < 1
            || parsed > 65535)
        {
            return Fail(
                $"'{rest[1..]}' is not a port · a port is 1 to 65535, and a Star printer listens on {DefaultRawPrintingPort}",
                out error);
        }

        port = parsed;
        return true;
    }

    /// <summary>
    /// Drop a scheme and anything after the authority, so a pasted
    /// <c>http://192.168.1.50/config</c> is read as the address it contains. Nothing is
    /// inferred from the scheme — a printer's web page is on 80 and its print port is
    /// 9100, so keeping the scheme's port would be the wrong number.
    /// </summary>
    private static string StripUrl(string text)
    {
        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        var authority = scheme >= 0 ? text[(scheme + 3)..] : text;

        var slash = authority.IndexOf('/');
        if (slash >= 0)
        {
            authority = authority[..slash];
        }

        // A pasted URL can carry credentials. They are not part of an address a printer
        // is dialled at, and storing them would put a password in the venue's registry.
        var at = authority.LastIndexOf('@');
        if (at >= 0)
        {
            authority = authority[(at + 1)..];
        }

        return authority.Trim();
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }
}
