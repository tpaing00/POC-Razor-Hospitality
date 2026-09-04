using Restaurant.Shared.Models.Dtos;

namespace Restaurant.UI.Shared.Services.Printing;

/// <summary>
/// <see cref="IReceiptPrinter"/> over any <see cref="IPrinterTransport"/>. The whole
/// of the print state machine, and none of the platform.
///
/// This is the class that makes the seam pay for itself: it holds the seven states,
/// the connect-write-read-dispose sequence, the status decode and every sentence the
/// screen renders, and it never learns whether the bytes left over a radio or over a
/// socket. Adding the network transport the TSP143IV-UEWB also offers means writing
/// one <see cref="IPrinterTransport"/> and registering it. Nothing here changes, and
/// <see cref="BagTicket"/> does not change either.
///
/// **Nothing in here runs on the UI thread.** Every platform call is behind an await,
/// and the transport is responsible for keeping its own blocking work off the caller's
/// thread. A failure raises <see cref="Changed"/> with a condition the screen renders;
/// nothing is written to a log a server on a terminal will never see.
/// </summary>
public sealed class TransportReceiptPrinter : IReceiptPrinter
{
    private readonly IPrinterTransport _transport;
    private readonly IPrinterPreference _preference;

    /// <summary>
    /// One job at a time. Two overlapping writes down one RFCOMM socket interleave into
    /// a label carrying half of each, and the test control plus an order landing at the
    /// same moment is exactly how that happens in a service.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private PrinterCondition _condition = PrinterCondition.NoPrinter;
    private PrinterDevice? _selected;
    private IReadOnlyList<PrinterDevice> _found = Array.Empty<PrinterDevice>();

    /// <summary>
    /// How long to wait for the printer to push a status block after a job. Automatic
    /// Status Back is not a request-response exchange — the printer sends when it has
    /// something to say — so this is a listening window rather than a timeout, and
    /// hearing nothing is a normal outcome that the condition records as unreadable
    /// rather than as healthy.
    /// </summary>
    private static readonly TimeSpan StatusWindow = TimeSpan.FromMilliseconds(1200);

    public TransportReceiptPrinter(IPrinterTransport transport, IPrinterPreference preference)
    {
        _transport = transport;
        _preference = preference;

        // A remembered pairing comes back as NotTested, never as Ready. The preference
        // records which printer was chosen; it records nothing about whether that
        // printer is switched on this morning.
        if (_preference.DeviceId is { Length: > 0 } id)
        {
            _selected = new PrinterDevice(id, _preference.DeviceName ?? id);
            _condition = new PrinterCondition(
                PrinterState.NotTested,
                "This printer is remembered from last time. Print a test label to confirm it answers.");
        }
    }

    public PrinterCondition Condition => _condition;

    public PrinterDevice? Selected => _selected;

    public IReadOnlyList<PrinterDevice> Found => _found;

    public bool IsSupported => _transport.IsAvailable(out _);

    public event EventHandler? Changed;

    public async Task DiscoverAsync(CancellationToken cancellationToken = default)
    {
        if (!_transport.IsAvailable(out var reason))
        {
            Set(new PrinterCondition(PrinterState.NoPrinter, reason ?? UnavailableLine));
            return;
        }

        Set(new PrinterCondition(PrinterState.Searching));

        try
        {
            _found = await _transport.DiscoverAsync(cancellationToken).ConfigureAwait(false);

            // Discovery is not a print, so it never produces Ready. It returns the
            // screen to whatever it was before the scan: a selection that has not been
            // tested, or no printer at all.
            Set(_selected is null
                ? PrinterCondition.NoPrinter
                : new PrinterCondition(PrinterState.NotTested, NotTestedLine));
        }
        catch (OperationCanceledException)
        {
            Set(_selected is null ? PrinterCondition.NoPrinter : new PrinterCondition(PrinterState.NotTested));
        }
        catch (Exception ex)
        {
            Set(new PrinterCondition(PrinterState.Failed, $"Could not search for printers · {Reason(ex)}"));
        }
    }

    public Task SelectAsync(PrinterDevice device, CancellationToken cancellationToken = default)
    {
        _selected = device;
        _preference.Remember(device.Id, device.Name);

        // NotTested, not Ready. Picking a name out of a list proves the platform knows
        // the device; only a byte that went out and came back proves it prints.
        Set(new PrinterCondition(
            PrinterState.NotTested,
            device.IsPaired
                ? NotTestedLine
                : "This printer is not paired with the tablet yet. Printing will raise Android's pairing prompt."));

        return Task.CompletedTask;
    }

    public Task ForgetAsync(CancellationToken cancellationToken = default)
    {
        _selected = null;
        _preference.Forget();
        Set(PrinterCondition.NoPrinter);
        return Task.CompletedTask;
    }

    public Task<PrintOutcome> PrintBagTicketAsync(OrderDto order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        var payload = BagTicket.Build(order, BagTicket.LocalPlacedAt(order.CreatedAt));
        return SendAsync(payload, cancellationToken);
    }

    public Task<PrintOutcome> PrintTestLabelAsync(string terminalId, CancellationToken cancellationToken = default)
    {
        var payload = BagTicket.BuildTestLabel(terminalId, DateTime.Now);
        return SendAsync(payload, cancellationToken);
    }

    /// <summary>
    /// Connect, enable status reporting, write, listen, close. Every job takes a fresh
    /// connection: an RFCOMM socket held open across a shift is how a terminal ends up
    /// unable to reconnect after somebody power-cycles the printer.
    /// </summary>
    private async Task<PrintOutcome> SendAsync(byte[] payload, CancellationToken cancellationToken)
    {
        if (!_transport.IsAvailable(out var reason))
        {
            var blocked = new PrinterCondition(PrinterState.NoPrinter, reason ?? UnavailableLine);
            Set(blocked);
            return new PrintOutcome(false, blocked);
        }

        if (_selected is null)
        {
            Set(PrinterCondition.NoPrinter);
            return new PrintOutcome(false, PrinterCondition.NoPrinter);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Set(new PrinterCondition(PrinterState.Printing));

            IPrinterConnection connection;
            try
            {
                connection = await _transport.ConnectAsync(_selected.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return Fail(new PrinterCondition(
                    PrinterState.Unreachable,
                    $"Printer unreachable · check it is powered on and in range ({Reason(ex)})"));
            }

            try
            {
                // Automatic Status Back is switched on first and separately, so the
                // block the printer pushes in reply is already on its way while the
                // ticket is being written.
                await connection.WriteAsync(StarLine.AutomaticStatusOn, cancellationToken).ConfigureAwait(false);
                await connection.WriteAsync(payload, cancellationToken).ConfigureAwait(false);

                var reply = await connection.ReadAsync(StatusWindow, cancellationToken).ConfigureAwait(false);
                var status = StarLineStatus.Parse(reply);

                var fault = status.ToCondition();
                if (fault is not null)
                {
                    return Fail(fault);
                }

                // The bytes went out. Whether the printer reported its own condition is
                // recorded rather than assumed: a silent printer is not a healthy one,
                // it is one that did not say.
                var done = new PrinterCondition(
                    PrinterState.Ready,
                    status.PaperNearEmpty
                        ? "Printed · the roll is nearly out, change it before service"
                        : status.IsKnown
                            ? null
                            : "Printed · the printer did not report its condition, so paper is unconfirmed",
                    StatusWasReadable: status.IsKnown);

                Set(done);
                return new PrintOutcome(true, done);
            }
            catch (Exception ex)
            {
                return Fail(new PrinterCondition(PrinterState.Failed, $"Could not print · {Reason(ex)}"));
            }
            finally
            {
                try
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Closing a socket that already failed is not a second failure to
                    // report. The condition the caller sees is the one that broke.
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private PrintOutcome Fail(PrinterCondition condition)
    {
        Set(condition);
        return new PrintOutcome(false, condition);
    }

    private void Set(PrinterCondition condition)
    {
        _condition = condition;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private const string NotTestedLine =
        "Selected, and not yet tested. Print a test label to confirm it answers.";

    private const string UnavailableLine =
        "This host has no printer to reach.";

    /// <summary>
    /// The cause, in a person's words, on one line. The same treatment Order entry's
    /// send failure takes: the exception's own message, trimmed to something that fits
    /// beside the next move, rather than a type name.
    /// </summary>
    private static string Reason(Exception ex)
    {
        var message = ex.Message?.Trim();
        if (string.IsNullOrEmpty(message))
        {
            return ex.GetType().Name;
        }

        message = message.Replace('\r', ' ').Replace('\n', ' ');
        return message.Length > 120 ? message[..117] + "..." : message;
    }
}
