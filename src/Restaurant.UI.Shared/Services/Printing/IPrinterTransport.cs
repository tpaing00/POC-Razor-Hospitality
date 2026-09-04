namespace Restaurant.UI.Shared.Services.Printing;

/// <summary>
/// What carries bytes to a printer. The seam this whole feature is built around.
///
/// **A network printer is a second implementation of this interface and nothing
/// else.** The ticket builder, the setup screen, the state machine and the Star Line
/// command bytes all sit above it and none of them knows what is underneath, because
/// a socket is a socket whether the far end is a radio or an IP address. The
/// TSP143IV-UEWB has Wi-Fi, Bluetooth, USB and Ethernet on the same box; today
/// <c>Restaurant.Mobile</c> registers the Bluetooth Classic RFCOMM implementation and
/// the rest are one class each.
///
/// <c>Restaurant.UI.Shared</c> owns the interface and implements none of it, which is
/// the same arrangement §12 sets up for <c>IDeviceStatus</c>: the library asks, the
/// host answers, and the dependency never points the other way.
/// </summary>
public interface IPrinterTransport
{
    /// <summary>What this transport is, for the one line the setup screen prints under
    /// the block. "Bluetooth", "Network".</summary>
    string Name { get; }

    /// <summary>
    /// Whether the transport can be used at all right now — the radio is on, the
    /// permission is granted, the platform has the API. A transport that cannot run
    /// says so in <paramref name="reason"/> as one line naming the cause and the next
    /// move (§10), and the screen renders that instead of an error.
    /// </summary>
    bool IsAvailable(out string? reason);

    /// <summary>
    /// The devices this transport can offer. Over Bluetooth that is the bonded set
    /// plus whatever an inquiry turns up; over TCP it would be whatever answered a
    /// subnet probe, or the one address a person typed.
    /// </summary>
    Task<IReadOnlyList<PrinterDevice>> DiscoverAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Open a connection to one device. Throws on failure, carrying the platform's own
    /// message — the caller turns that into a <see cref="PrinterCondition"/> rather
    /// than paraphrasing a fault it did not see.
    /// </summary>
    Task<IPrinterConnection> ConnectAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// This transport's availability as a row the screen can render, which for a
    /// single transport is <see cref="IsAvailable"/> restated and for a composite is
    /// one row per member.
    ///
    /// **The composite is why this is not just <see cref="IsAvailable"/>.** A back
    /// office running a network transport and a Bluetooth one is available when
    /// either works, so the composite's single bool is true while the radio is
    /// missing — and a screen with only that bool would have nowhere to say the radio
    /// is missing, leaving a person to read an empty list as "no printers in the
    /// building". Rows keep the two facts apart.
    ///
    /// The default is the honest answer for a transport that is one thing, so no
    /// existing implementation has to write it.
    /// </summary>
    IReadOnlyList<TransportAvailability> Describe()
    {
        var available = IsAvailable(out var reason);
        return new[] { new TransportAvailability(Name, available, available ? null : reason) };
    }

    /// <summary>
    /// The id <see cref="ConnectAsync"/> would take for a printer at
    /// <paramref name="address"/> reached over the transport called
    /// <paramref name="transportName"/>, or null when this host has no such transport.
    ///
    /// **This exists because the venue's registry stores a transport and an address,
    /// not an id.** A registry row has to survive being read by a different host from
    /// the one that wrote it, so it records the two durable facts — <c>Network</c> and
    /// <c>192.168.1.50:9100</c> — rather than a routing key that is an artifact of one
    /// host's registration order. Turning those back into an id is the transport's job,
    /// because the transport is what invented the prefix.
    ///
    /// Null rather than a throw, and null rather than a guess: a back office on a host
    /// with no Bluetooth radio can still list a Bluetooth printer the venue owns, and
    /// the honest answer to "print to it from here" is that this host cannot reach it —
    /// which is a sentence the caller writes, not an exception.
    ///
    /// The default is the answer for a transport that is one thing: it takes its own
    /// address unchanged and answers for nobody else's.
    /// </summary>
    string? RouteTo(string transportName, string address) =>
        string.Equals(Name, transportName, StringComparison.OrdinalIgnoreCase) ? address : null;
}

/// <summary>
/// A transport that can also take an address a person typed.
///
/// **Discovery on a restaurant network is allowed to fail, and this is what it fails
/// to.** Multicast is dropped between VLANs, blocked by client isolation on a
/// guest-facing access point, and absent entirely on a printer somebody gave a static
/// address on a different subnet. In every one of those cases a person can still read
/// the address off the printer's self-test page, and a discovery design that has no
/// answer for that is a design that strands them.
///
/// It is a separate interface rather than a member of <see cref="IPrinterTransport"/>
/// because typing a Bluetooth MAC is not a thing anybody should be asked to do: the
/// bond is the platform's record and there is no address to read off the printer.
/// A transport that cannot honestly accept a typed address does not implement this,
/// and the screen does not offer the field.
/// </summary>
public interface IAddressablePrinterTransport : IPrinterTransport
{
    /// <summary>
    /// Turn what a person typed into a device, or throw with a sentence saying what
    /// is wrong with it.
    ///
    /// **It validates and does not probe.** A printer that is switched off at
    /// half past four still has the address it will have at five, and refusing to
    /// record it because nothing answered would be the screen deciding a fault it has
    /// not diagnosed. The device comes back <see cref="PrinterState.NotTested"/> like
    /// every other selection, and the test label is what proves it.
    /// </summary>
    Task<PrinterDevice> ResolveAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether an address typed right now would go anywhere. For a transport that is
    /// one thing this is simply whether it is available; a composite overrides it,
    /// because a composite is available when any member is and only some members take
    /// addresses.
    /// </summary>
    bool AcceptsAddress => IsAvailable(out _);
}

/// <summary>
/// One open connection. Write bytes, optionally read what the printer pushes back.
/// Disposed after every job: holding a Bluetooth socket open across a shift is how a
/// terminal ends up unable to reconnect after the printer power-cycles.
/// </summary>
public interface IPrinterConnection : IAsyncDisposable
{
    Task WriteAsync(byte[] payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read whatever the printer has already sent, up to <paramref name="wait"/>.
    /// Returns an empty array when nothing arrived, which is a normal answer and not a
    /// failure: a printer with Automatic Status Back switched off says nothing, and
    /// the caller reports that the status was unreadable rather than reporting health.
    /// </summary>
    Task<byte[]> ReadAsync(TimeSpan wait, CancellationToken cancellationToken = default);
}

/// <summary>
/// Where the chosen printer is remembered between launches. One address and one name,
/// which is the whole of it — anything larger is the venue's device registry, which
/// the handbook's Devices spec rules is additive API work rather than this screen's
/// job.
/// </summary>
public interface IPrinterPreference
{
    string? DeviceId { get; }

    string? DeviceName { get; }

    void Remember(string deviceId, string deviceName);

    void Forget();
}

/// <summary>
/// The preference for a host with nowhere to put one. It remembers for as long as the
/// process lives and no longer, which is the honest behaviour for the back office's
/// preview — a browser tab has no printer to remember.
/// </summary>
public sealed class InMemoryPrinterPreference : IPrinterPreference
{
    public string? DeviceId { get; private set; }

    public string? DeviceName { get; private set; }

    public void Remember(string deviceId, string deviceName)
    {
        DeviceId = deviceId;
        DeviceName = deviceName;
    }

    public void Forget()
    {
        DeviceId = null;
        DeviceName = null;
    }
}
