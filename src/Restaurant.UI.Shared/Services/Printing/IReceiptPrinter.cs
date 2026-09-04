using Restaurant.Shared.Models.Dtos;

namespace Restaurant.UI.Shared.Services.Printing;

/// <summary>
/// What a screen asks about printing. Handbook §12 · "Printing from a shared
/// component", and the second reading <c>Restaurant.UI.Shared</c> owns the question to
/// without being able to answer it.
///
/// This library has no MAUI reference and no platform of its own, so it cannot open a
/// Bluetooth socket any more than it could call <c>Battery.Default</c>. It declares
/// the question here, <c>Restaurant.Mobile</c> registers the implementation that has a
/// radio, and the back office registers <see cref="UnavailableReceiptPrinter"/>, which
/// answers honestly that there is no printer. The dependency points from the host to
/// this library and never the other way.
/// </summary>
public interface IReceiptPrinter
{
    /// <summary>The printer's condition right now, and the sentence the screen renders
    /// under the chip.</summary>
    PrinterCondition Condition { get; }

    /// <summary>The chosen printer, or null when none is chosen.</summary>
    PrinterDevice? Selected { get; }

    /// <summary>What the last discovery found. Empty until one has run.</summary>
    IReadOnlyList<PrinterDevice> Found { get; }

    /// <summary>Whether this host can print at all — false in the back office's
    /// preview, and false on a device whose Bluetooth permission has been refused. The
    /// reason is on <see cref="Condition"/>.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// One row per transport, saying whether it can be used and why not when it
    /// cannot.
    ///
    /// **This is the property that stops "no radio on this host" being drawn as "no
    /// printers found".** The terminal has one transport and <see cref="IsSupported"/>
    /// carries the whole answer; the back office has two or more, is supported when
    /// either works, and needs somewhere to say that the network is searching while
    /// the Bluetooth radio is absent. A screen renders these rows whenever there is
    /// more than one, and renders nothing extra when there is one.
    /// </summary>
    IReadOnlyList<TransportAvailability> Transports { get; }

    /// <summary>Whether any transport can take an address a person typed. False on the
    /// terminal, where a Bluetooth bond has no address to type.</summary>
    bool SupportsAddressEntry { get; }

    /// <summary>
    /// Record a printer at an address somebody read off its self-test page, and select
    /// it. The degradation path for a network where multicast does not cross.
    ///
    /// Never throws: an address that will not parse leaves the condition carrying what
    /// is wrong with it. The device is selected and reads
    /// <see cref="PrinterState.NotTested"/>, because typing an address proves the
    /// address is well formed and nothing else.
    /// </summary>
    Task AddByAddressAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>Ask the transport what it can see. Never throws: a discovery that fails
    /// leaves the condition carrying why.</summary>
    Task DiscoverAsync(CancellationToken cancellationToken = default);

    /// <summary>Choose a printer and remember it. The state goes to
    /// <see cref="PrinterState.NotTested"/>, never to Ready — picking a device out of a
    /// list proves the device is bonded, not that it prints.</summary>
    Task SelectAsync(PrinterDevice device, CancellationToken cancellationToken = default);

    /// <summary>Forget the chosen printer.</summary>
    Task ForgetAsync(CancellationToken cancellationToken = default);

    /// <summary>Print one bag ticket for an order the API has already accepted.</summary>
    Task<PrintOutcome> PrintBagTicketAsync(OrderDto order, CancellationToken cancellationToken = default);

    /// <summary>Print the test label. The one action on the setup screen that proves
    /// anything.</summary>
    Task<PrintOutcome> PrintTestLabelAsync(string terminalId, CancellationToken cancellationToken = default);

    /// <summary>Raised whenever <see cref="Condition"/>, <see cref="Selected"/> or
    /// <see cref="Found"/> moves. The screen re-renders off this rather than
    /// polling.</summary>
    event EventHandler? Changed;
}

/// <summary>
/// The answer for a host with no printer to reach: nothing found, nothing selected,
/// nothing printed, and a sentence saying why.
///
/// It is named for what it is rather than for the one host that registers it today,
/// on the same rule §12 states for <c>UnknownDeviceStatus</c> — it is this library's
/// answer for any host without a printer, the back office's preview now and a test or
/// a WASM build later.
///
/// **This is not a stand-in for a printer. It is the reason there is no stand-in for a
/// printer.** A preview that reported a paired device, accepted a test print and drew
/// a green chip would be the one placeholder in this product a person acts on: they
/// would go to the venue believing the pairing worked. So it refuses, and says the
/// refusal is about this host rather than about the printer.
/// </summary>
public sealed class UnavailableReceiptPrinter : IReceiptPrinter
{
    private static readonly PrinterCondition NoHost = new(
        PrinterState.NoPrinter,
        "This host has no printer. The terminal prints over Bluetooth; a browser has no radio to reach one.");

    public PrinterCondition Condition => NoHost;

    public PrinterDevice? Selected => null;

    public IReadOnlyList<PrinterDevice> Found => Array.Empty<PrinterDevice>();

    public bool IsSupported => false;

    /// <summary>One row, saying the same thing the condition says. A host with no
    /// printer has exactly one fact to report about transports, and it is that it has
    /// none.</summary>
    public IReadOnlyList<TransportAvailability> Transports { get; } =
        new[] { new TransportAvailability("None", false, NoHost.Message) };

    public bool SupportsAddressEntry => false;

    public Task AddByAddressAsync(string address, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task DiscoverAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SelectAsync(PrinterDevice device, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ForgetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<PrintOutcome> PrintBagTicketAsync(OrderDto order, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PrintOutcome(false, NoHost));

    public Task<PrintOutcome> PrintTestLabelAsync(string terminalId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PrintOutcome(false, NoHost));

    /// <summary>Never raised. The accessors are written out so the compiler does not
    /// warn about an event field nothing assigns.</summary>
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}
