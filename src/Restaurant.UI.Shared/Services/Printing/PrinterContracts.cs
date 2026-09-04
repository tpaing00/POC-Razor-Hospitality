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
public sealed record PrinterDevice(string Id, string Name, bool IsPaired = true);

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
