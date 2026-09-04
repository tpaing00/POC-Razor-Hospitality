using Restaurant.Shared.Models.Dtos;
using Restaurant.UI.Shared.Services.Printing;
using Xunit;

namespace Restaurant.Printing.Tests;

/// <summary>
/// The print state machine, driven by a transport made of nothing.
///
/// This is what the <see cref="IPrinterTransport"/> seam buys, demonstrated: the whole
/// of <see cref="TransportReceiptPrinter"/> — the seven states, the connect-write-read
/// sequence, the status decode and every sentence the screen renders — is exercised
/// here with no radio, no socket and no Android. A network transport for the
/// TSP143IV's Ethernet and Wi-Fi side would be a second class implementing the same
/// interface, and these tests would not change.
/// </summary>
public class TransportReceiptPrinterTests
{
    private static OrderDto Order() => new()
    {
        Id = 1,
        OrderNumber = "ORD-1",
        CreatedAt = DateTime.UtcNow,
        Items = { new OrderItemDto { MenuItemName = "Fries", Quantity = 1 } }
    };

    private static PrinterDevice Device => new("00:11:22:33:44:55", "Star TSP143IV");

    // ─── Selection ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Selecting_a_printer_never_reports_it_ready()
    {
        var printer = new TransportReceiptPrinter(new FakeTransport(), new InMemoryPrinterPreference());

        await printer.SelectAsync(Device);

        // Picking a name out of a list proves the platform knows the device. Only a
        // byte that went out and came back proves it prints. §12's rule about the
        // invented battery, applied to a second reading.
        Assert.Equal(PrinterState.NotTested, printer.Condition.State);
    }

    [Fact]
    public async Task A_remembered_pairing_comes_back_as_not_tested()
    {
        var preference = new InMemoryPrinterPreference();
        preference.Remember(Device.Id, Device.Name);

        var printer = new TransportReceiptPrinter(new FakeTransport(), preference);

        Assert.Equal(Device.Id, printer.Selected!.Id);
        Assert.Equal(PrinterState.NotTested, printer.Condition.State);
    }

    [Fact]
    public async Task Forgetting_clears_the_selection_and_the_preference()
    {
        var preference = new InMemoryPrinterPreference();
        var printer = new TransportReceiptPrinter(new FakeTransport(), preference);

        await printer.SelectAsync(Device);
        await printer.ForgetAsync();

        Assert.Null(printer.Selected);
        Assert.Null(preference.DeviceId);
        Assert.Equal(PrinterState.NoPrinter, printer.Condition.State);
    }

    [Fact]
    public async Task Discovery_does_not_produce_ready_either()
    {
        var transport = new FakeTransport { Devices = { Device } };
        var printer = new TransportReceiptPrinter(transport, new InMemoryPrinterPreference());

        await printer.DiscoverAsync();

        Assert.Single(printer.Found);
        Assert.Equal(PrinterState.NoPrinter, printer.Condition.State);
    }

    // ─── Printing ────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_successful_write_reaches_the_transport_with_a_cut_on_the_end()
    {
        var transport = new FakeTransport();
        var printer = new TransportReceiptPrinter(transport, new InMemoryPrinterPreference());
        await printer.SelectAsync(Device);

        var outcome = await printer.PrintBagTicketAsync(Order());

        Assert.True(outcome.Printed);
        Assert.Equal(PrinterState.Ready, printer.Condition.State);

        // Status reporting is switched on first and separately, so the block the
        // printer pushes in reply is already on its way while the ticket is written.
        Assert.Equal(StarLine.AutomaticStatusOn, transport.Writes[0]);
        Assert.Equal(StarLine.CutFull, transport.Writes[1][^StarLine.CutFull.Length..]);
    }

    [Fact]
    public async Task A_silent_printer_is_reported_as_unconfirmed_rather_than_healthy()
    {
        var printer = new TransportReceiptPrinter(new FakeTransport(), new InMemoryPrinterPreference());
        await printer.SelectAsync(Device);

        var outcome = await printer.PrintBagTicketAsync(Order());

        // The bytes went out and nothing came back. That is not the same as the
        // printer being fine, and the line says so.
        Assert.False(outcome.Condition.StatusWasReadable);
        Assert.Contains("did not report its condition", outcome.Condition.Message);
    }

    [Fact]
    public async Task Paper_out_comes_back_off_the_status_block()
    {
        var transport = new FakeTransport
        {
            Reply = new byte[] { 0x20, 0x40, 0x40, 0x40, 0x48, 0x40, 0x40 }
        };
        var printer = new TransportReceiptPrinter(transport, new InMemoryPrinterPreference());
        await printer.SelectAsync(Device);

        var outcome = await printer.PrintBagTicketAsync(Order());

        Assert.False(outcome.Printed);
        Assert.Equal(PrinterState.PaperOut, printer.Condition.State);
        Assert.Contains("load a roll", printer.Condition.Message);
    }

    [Fact]
    public async Task A_refused_connection_is_unreachable_and_carries_the_platforms_reason()
    {
        var transport = new FakeTransport { ConnectFailure = "socket closed by peer" };
        var printer = new TransportReceiptPrinter(transport, new InMemoryPrinterPreference());
        await printer.SelectAsync(Device);

        var outcome = await printer.PrintTestLabelAsync("Counter 2");

        Assert.False(outcome.Printed);
        Assert.Equal(PrinterState.Unreachable, printer.Condition.State);
        // §10: the cause and the next move, in one line.
        // "In range" was true when the only transport was a radio. The same sentence is
        // now shown for a network printer, so it says what is true of either.
        Assert.Contains("check it is switched on and reachable from this host", outcome.Condition.Message);
        Assert.Contains("socket closed by peer", outcome.Condition.Message);
    }

    [Fact]
    public async Task A_failed_write_surfaces_rather_than_disappearing()
    {
        var transport = new FakeTransport { WriteFailure = "broken pipe" };
        var printer = new TransportReceiptPrinter(transport, new InMemoryPrinterPreference());
        await printer.SelectAsync(Device);

        var outcome = await printer.PrintTestLabelAsync("Counter 2");

        Assert.Equal(PrinterState.Failed, printer.Condition.State);
        Assert.Contains("broken pipe", outcome.Condition.Message);
    }

    [Fact]
    public async Task Printing_with_no_printer_selected_refuses_instead_of_throwing()
    {
        var printer = new TransportReceiptPrinter(new FakeTransport(), new InMemoryPrinterPreference());

        var outcome = await printer.PrintBagTicketAsync(Order());

        Assert.False(outcome.Printed);
        Assert.Equal(PrinterState.NoPrinter, outcome.Condition.State);
    }

    [Fact]
    public async Task An_unavailable_transport_is_reported_and_never_dialled()
    {
        var transport = new FakeTransport { Unavailable = "Bluetooth is off · switch it on in Android settings" };
        var printer = new TransportReceiptPrinter(transport, new InMemoryPrinterPreference());
        await printer.SelectAsync(Device);

        var outcome = await printer.PrintTestLabelAsync("Counter 2");

        Assert.False(outcome.Printed);
        Assert.Equal(0, transport.Connects);
        Assert.Contains("Bluetooth is off", outcome.Condition.Message);
    }

    [Fact]
    public async Task Every_connection_is_closed_even_when_the_write_fails()
    {
        var transport = new FakeTransport { WriteFailure = "broken pipe" };
        var printer = new TransportReceiptPrinter(transport, new InMemoryPrinterPreference());
        await printer.SelectAsync(Device);

        await printer.PrintTestLabelAsync("Counter 2");

        // A socket held open across a shift is how a terminal ends up unable to
        // reconnect after somebody power-cycles the printer.
        Assert.Equal(transport.Connects, transport.Disposals);
    }

    [Fact]
    public async Task Two_jobs_at_once_do_not_interleave_on_the_socket()
    {
        var transport = new FakeTransport { WriteDelay = TimeSpan.FromMilliseconds(30) };
        var printer = new TransportReceiptPrinter(transport, new InMemoryPrinterPreference());
        await printer.SelectAsync(Device);

        await Task.WhenAll(
            printer.PrintBagTicketAsync(Order()),
            printer.PrintTestLabelAsync("Counter 2"));

        // Two overlapping writes down one RFCOMM socket produce a label carrying half
        // of each. The gate is what stops the test control and an order landing
        // together from doing that.
        Assert.Equal(2, transport.Connects);
        Assert.False(transport.Overlapped);
    }

    [Fact]
    public async Task The_state_moves_through_printing_and_the_screen_is_told()
    {
        var transport = new FakeTransport();
        var printer = new TransportReceiptPrinter(transport, new InMemoryPrinterPreference());
        var seen = new List<PrinterState>();
        printer.Changed += (_, _) => seen.Add(printer.Condition.State);

        await printer.SelectAsync(Device);
        await printer.PrintTestLabelAsync("Counter 2");

        Assert.Contains(PrinterState.Printing, seen);
        Assert.Equal(PrinterState.Ready, seen[^1]);
    }

    // ─── The stub host ───────────────────────────────────────────────────────

    [Fact]
    public async Task The_back_office_printer_refuses_and_says_it_is_the_host()
    {
        var printer = new UnavailableReceiptPrinter();

        var outcome = await printer.PrintBagTicketAsync(Order());

        Assert.False(printer.IsSupported);
        Assert.False(outcome.Printed);
        Assert.Empty(printer.Found);
        Assert.Null(printer.Selected);
        // The refusal is about this host, not about the printer.
        Assert.Contains("browser has no radio", outcome.Condition.Message);
    }

    /// <summary>
    /// A transport made of nothing: it records what was written, answers with whatever
    /// reply it was given, and can be told to fail at either step.
    /// </summary>
    private sealed class FakeTransport : IPrinterTransport
    {
        public List<PrinterDevice> Devices { get; } = new();

        public List<byte[]> Writes { get; } = new();

        public byte[] Reply { get; set; } = Array.Empty<byte>();

        public string? Unavailable { get; set; }

        public string? ConnectFailure { get; set; }

        public string? WriteFailure { get; set; }

        public TimeSpan WriteDelay { get; set; }

        public int Connects;

        public int Disposals;

        public bool Overlapped;

        private int _open;

        public string Name => "Fake";

        public bool IsAvailable(out string? reason)
        {
            reason = Unavailable;
            return Unavailable is null;
        }

        public Task<IReadOnlyList<PrinterDevice>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PrinterDevice>>(Devices);

        public Task<IPrinterConnection> ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (ConnectFailure is not null)
            {
                throw new IOException(ConnectFailure);
            }

            Connects++;
            if (Interlocked.Increment(ref _open) > 1)
            {
                Overlapped = true;
            }

            return Task.FromResult<IPrinterConnection>(new FakeConnection(this));
        }

        private sealed class FakeConnection(FakeTransport owner) : IPrinterConnection
        {
            public async Task WriteAsync(byte[] payload, CancellationToken cancellationToken = default)
            {
                if (owner.WriteDelay > TimeSpan.Zero)
                {
                    await Task.Delay(owner.WriteDelay, cancellationToken);
                }

                if (owner.WriteFailure is not null && payload.Length > 8)
                {
                    throw new IOException(owner.WriteFailure);
                }

                owner.Writes.Add(payload);
            }

            public Task<byte[]> ReadAsync(TimeSpan wait, CancellationToken cancellationToken = default) =>
                Task.FromResult(owner.Reply);

            public ValueTask DisposeAsync()
            {
                owner.Disposals++;
                Interlocked.Decrement(ref owner._open);
                return ValueTask.CompletedTask;
            }
        }
    }
}
