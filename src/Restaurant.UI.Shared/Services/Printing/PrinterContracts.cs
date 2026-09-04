namespace Restaurant.UI.Shared.Services.Printing;

/// <summary>
/// What the printer setup screen is allowed to say about the printer, and nothing
/// else. Handbook Part II-A · Printer setup carries the same seven as a table with
/// the sentence each one renders.
///
/// <see cref="NotTested"/> is the one that needs defending. It exists for the same
/// reason <c>IDeviceStatus.BatteryLevel</c> is a <c>double?</c> (§12): a printer
/// that has been chosen out of a list and never talked to is not a working printer,
/// and drawing it <see cref="Ready"/> would be the invented battery in another
/// costume. Selecting a device proves the device is bonded; only a byte that came
/// back proves it prints.
/// </summary>
public enum PrinterState
{
    /// <summary>Nothing selected and nothing remembered.</summary>
    NoPrinter,

    /// <summary>The platform is enumerating. Transient, and the only state the scan
    /// control is disabled in.</summary>
    Searching,

    /// <summary>A printer is selected. Nothing has been sent to it yet, so nothing
    /// is known about it beyond its name.</summary>
    NotTested,

    /// <summary>The last exchange succeeded.</summary>
    Ready,

    /// <summary>A job is on the wire.</summary>
    Printing,

    /// <summary>The socket would not open, or the write failed at the transport.
    /// The printer is off, out of range, or claimed by something else.</summary>
    Unreachable,

    /// <summary>The printer answered and its own status said it cannot print — the
    /// paper ran out, or the cover is open.</summary>
    PaperOut,

    /// <summary>Anything else. Carries the platform's reason rather than a
    /// paraphrase of it.</summary>
    Failed
}

/// <summary>
/// One candidate printer, as the transport found it.
/// </summary>
/// <param name="Id">The transport's own address for the device — a MAC address over
/// Bluetooth, and a host and port over TCP. Opaque to everything above the
/// transport, and the value that is remembered between launches.</param>
/// <param name="Name">What to show a person. Never parsed.</param>
/// <param name="IsPaired">Whether the platform already holds a bond for this device.
/// A bonded device connects without a system dialog; an unbonded one raises Android's
/// pairing prompt on the first connect, which is a thing the screen has to be able to
/// say before it happens.</param>
/// <param name="Transport">Which transport found it — "Bluetooth", "Network". Empty
/// on a host that runs one transport, where naming it would be noise. The back
/// office runs several at once and a person choosing between two printers with
/// similar names needs to know which one is on the network and which is on the
/// radio.</param>
/// <param name="Address">The address to show a person, when that is not the same as
/// <paramref name="Id"/>. Aggregating several transports makes <paramref name="Id"/>
/// a routing key with a transport prefix on it, which is correct for
/// <see cref="IPrinterTransport.ConnectAsync"/> and wrong on a screen; this is the
/// bare MAC or host and port. Empty means <paramref name="Id"/> is already
/// showable.</param>
/// <param name="PairingNote">What a person has to do before this device will accept a
/// job, in the transport's own words, or null where nothing is needed.
///
/// **It is the transport's sentence because pairing means different things.** On
/// Android an unbonded printer raises the system's pairing prompt on the first write,
/// so the person can just print. On a Windows host nothing prompts and they have to
/// pair it in Settings on the machine serving the page. On the network there is no
/// pairing at all — an address is either answered or it is not, and the test label is
/// what asks. A single sentence written above the transports would be wrong for two of
/// those three.</param>
public sealed record PrinterDevice(
    string Id,
    string Name,
    bool IsPaired = true,
    string Transport = "",
    string Address = "",
    string? PairingNote = null)
{
    /// <summary>The address as a person reads it. Never parsed, and never used to
    /// route a connection — that is <see cref="Id"/>'s job.</summary>
    public string Display => Address is { Length: > 0 } ? Address : Id;
}

/// <summary>
/// Whether one transport can be used right now, and why not when it cannot.
///
/// **This exists so that "this host has no Bluetooth radio" is never rendered as
/// "no printers found".** They are different facts: the first is about the host and
/// a person can act on it, the second is about the room. A back office aggregating
/// a network transport and a Bluetooth one has to be able to say that the network
/// found two printers and the radio is missing, in the same breath, without either
/// sentence standing in for the other.
/// </summary>
/// <param name="Name">The transport's own name.</param>
/// <param name="Available">Whether it can be used at all right now.</param>
/// <param name="Reason">One line naming the cause and the next move (§10), or null
/// when the transport is available.</param>
public sealed record TransportAvailability(string Name, bool Available, string? Reason);

/// <summary>
/// The printer's condition as one value: the state, and the sentence the screen
/// renders under it.
///
/// The message is built where the failure happened, because that is the only place
/// that knows the cause. §10 governs its shape — one line, the cause and the next
/// move, middot-separated — and a UI that reformats it would be guessing at a fault
/// it did not see.
/// </summary>
/// <param name="State">Which of the seven.</param>
/// <param name="Message">The line under the chip, or null where the state speaks for
/// itself.</param>
/// <param name="StatusWasReadable">Whether the printer reported its own condition on
/// the last exchange. False means the bytes went out and nothing came back, which is
/// not the same as the printer being fine — the screen says so rather than implying a
/// clean bill of health.</param>
public sealed record PrinterCondition(
    PrinterState State,
    string? Message = null,
    bool StatusWasReadable = false)
{
    public static readonly PrinterCondition NoPrinter =
        new(PrinterState.NoPrinter, "No printer paired. Pick one below and print a test label.");

    /// <summary>The word in the status chip. §4 owns the hue; this owns the word.</summary>
    public string Label => State switch
    {
        PrinterState.NoPrinter => "NO PRINTER",
        PrinterState.Searching => "SEARCHING",
        PrinterState.NotTested => "NOT TESTED",
        PrinterState.Ready => "READY",
        PrinterState.Printing => "PRINTING",
        PrinterState.Unreachable => "UNREACHABLE",
        PrinterState.PaperOut => "PAPER OUT",
        _ => "FAILED"
    };
}

/// <summary>
/// What one print attempt did. The condition is what the screen renders; the
/// boolean is for a caller that has to decide whether to say anything at all.
/// </summary>
public sealed record PrintOutcome(bool Printed, PrinterCondition Condition);
