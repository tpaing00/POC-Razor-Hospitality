namespace Restaurant.Shared.Models;

/// <summary>
/// How a registered printer is reached.
///
/// **The address is unreadable without this.** <c>192.168.1.50:9100</c> and
/// <c>00:11:62:4C:58:5D</c> are parsed by different code, validated by different rules
/// and dialled by different stacks, and a registry that stored only the string would be
/// asking every reader to guess which one it had. It is also what routes a job: the
/// back office aggregates its transports and addresses a device as
/// <c>network/192.168.1.50:9100</c>, so the transport name is half of the id.
///
/// The names match <c>IPrinterTransport.Name</c> on the two transports that exist
/// (<c>Network</c>, <c>Bluetooth</c>) because that string is what routes a connection.
/// A third transport is a new value here and a new implementation there, and the two
/// have to agree.
/// </summary>
public enum PrinterTransportKind
{
    /// <summary>Raw TCP on the venue LAN — Ethernet or Wi-Fi. The transport that
    /// matters in production, because a central back-office server can reach it from
    /// wherever it is racked.</summary>
    Network = 0,

    /// <summary>Bluetooth Classic RFCOMM. Reachable only from a host in the same room
    /// as the printer, which in production is the terminal rather than a central
    /// server.</summary>
    Bluetooth = 1
}
