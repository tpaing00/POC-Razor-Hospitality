using Restaurant.UI.Shared.Services.Printing;
using Xunit;

namespace Restaurant.Printing.Tests;

/// <summary>
/// Printing a test label to a printer the venue's registry named, rather than to the
/// one this host is paired with.
///
/// **The assertion this feature turns on is that nothing moves.** A manager testing
/// the bar printer from the back office must not repaint the chip describing the
/// printer the back office itself prints to, must not re-point it, and must not make
/// a terminal somewhere forget its own. The registry is the venue's list; the
/// selection stays the device's.
///
/// The other half is routing: a registry row stores a transport and an address, never
/// an id, because a routing key is an artifact of one host's registration order and a
/// registry row outlives the host that wrote it. Turning those two facts back into an
/// id is <see cref="IPrinterTransport.RouteTo"/>, and getting it wrong sends a label to
/// the wrong printer or to none.
/// </summary>
public class PrinterRegistryPrintingTests
{
    // ─── Routing an address back to an id ────────────────────────────────────

    [Fact]
    public void A_single_transport_takes_its_own_address_unchanged()
    {
        // The terminal's arrangement. One transport, no composite, no prefix: the
        // address IS the id. Called through the interface, because RouteTo is a default
        // interface method — a transport that never wrote one still answers, which is
        // why no existing transport had to change.
        IPrinterTransport transport = new RecordingTransport("Bluetooth");

        Assert.Equal("00:11:62:4C:58:5D", transport.RouteTo("Bluetooth", "00:11:62:4C:58:5D"));
    }

    [Fact]
    public void A_transport_answers_for_its_own_name_only()
    {
        // Null, not a throw and not a guess. A host with only a radio can still list a
        // network printer the venue owns; the honest answer to "print to it from here"
        // is that this host cannot reach it, which is a sentence the caller writes.
        IPrinterTransport transport = new RecordingTransport("Bluetooth");

        Assert.Null(transport.RouteTo("Network", "192.168.1.50:9100"));
    }

    [Fact]
    public void The_name_is_matched_without_regard_to_case()
    {
        // The registry stores an enum and renders it as "Network"; a transport calls
        // itself "Network". A build that later renamed either would be caught by the
        // null path rather than by a mismatch nobody sees.
        IPrinterTransport transport = new RecordingTransport("Network");

        Assert.Equal("192.168.1.50:9100", transport.RouteTo("network", "192.168.1.50:9100"));
    }

    [Fact]
    public void A_composite_puts_the_right_members_prefix_on_the_address()
    {
        var composite = new CompositePrinterTransport(new IPrinterTransport[]
        {
            new RecordingTransport("Network"),
            new RecordingTransport("Bluetooth")
        });

        Assert.Equal("network/192.168.1.50:9100", composite.RouteTo("Network", "192.168.1.50:9100"));
        Assert.Equal("bluetooth/00:11:62:4C:58:5D", composite.RouteTo("Bluetooth", "00:11:62:4C:58:5D"));
    }

    [Fact]
    public void A_transport_no_member_holds_routes_nowhere()
    {
        // The back office on a host with no radio, listing a Bluetooth printer the
        // venue owns. Null is what lets the screen say "this host has no Bluetooth
        // transport" instead of dialling something and reporting a failed connection
        // that never happened.
        var composite = new CompositePrinterTransport(new IPrinterTransport[]
        {
            new RecordingTransport("Network")
        });

        Assert.Null(composite.RouteTo("Bluetooth", "00:11:62:4C:58:5D"));
    }

    [Fact]
    public async Task A_routed_address_reaches_the_member_that_owns_it_with_its_own_id()
    {
        // The round trip. RouteTo builds the id and ConnectAsync takes it apart again;
        // if the two ever disagree the label goes to the wrong transport, which is a
        // failure that looks like a broken printer.
        var network = new RecordingTransport("Network");
        var bluetooth = new RecordingTransport("Bluetooth");
        var composite = new CompositePrinterTransport(new IPrinterTransport[] { network, bluetooth });

        var routed = composite.RouteTo("Bluetooth", "00:11:62:4C:58:5D");
        await composite.ConnectAsync(routed!);

        Assert.Null(network.ConnectedTo);
        Assert.Equal("00:11:62:4C:58:5D", bluetooth.ConnectedTo);
    }

    // ─── Printing without selecting ──────────────────────────────────────────

    [Fact]
    public async Task A_registry_test_print_reaches_the_address_it_was_given()
    {
        var transport = new RecordingTransport("Network");
        var printer = new TransportReceiptPrinter(transport, new InMemoryPrinterPreference());

        var outcome = await printer.PrintTestLabelToAsync("Network", "192.168.1.50:9100", "Bar");

        Assert.True(outcome.Printed);
        Assert.Equal("192.168.1.50:9100", transport.ConnectedTo);
        Assert.Equal(1, transport.Disposals);
    }

    [Fact]
    public async Task The_label_carries_the_name_it_was_fired_for()
    {
        // A label found on a bench beside three printers has to say which control
        // produced it, or the test proves the printer works and nothing about which.
        var transport = new RecordingTransport("Network");
        var printer = new TransportReceiptPrinter(transport, new InMemoryPrinterPreference());

        await printer.PrintTestLabelToAsync("Network", "192.168.1.50:9100", "Back office · Bar");

        var text = string.Join(string.Empty, transport.Writes.Select(System.Text.Encoding.ASCII.GetString));
        Assert.Contains("Back office", text);
        Assert.Contains("Bar", text);
    }

    [Fact]
    public async Task It_does_not_select_the_printer_it_printed_to()
    {
        var transport = new RecordingTransport("Network");
        var preference = new InMemoryPrinterPreference();
        var printer = new TransportReceiptPrinter(transport, preference);

        await printer.PrintTestLabelToAsync("Network", "192.168.1.50:9100", "Bar");

        // Nothing selected, nothing remembered. The venue's list is not a chooser.
        Assert.Null(printer.Selected);
        Assert.Null(preference.DeviceId);
        Assert.Equal(PrinterState.NoPrinter, printer.Condition.State);
    }

    [Fact]
    public async Task It_leaves_the_hosts_own_condition_exactly_where_it_was()
    {
        // THE ONE THAT MATTERS. The host prints its own test label and reads READY.
        // A manager then tests a registry printer that is unreachable. The host's chip
        // must still read READY, because nothing about the host's printer changed —
        // and a red chip here would send somebody to look at a printer that is fine.
        var transport = new RecordingTransport("Network");
        var printer = new TransportReceiptPrinter(transport, new InMemoryPrinterPreference());

        await printer.SelectAsync(new PrinterDevice("192.168.1.50:9100", "Counter"));
        await printer.PrintTestLabelAsync("Counter 2");
        Assert.Equal(PrinterState.Ready, printer.Condition.State);

        var changes = 0;
        printer.Changed += (_, _) => changes++;

        transport.ConnectFailure = "nothing answered at 10.0.0.9:9100 within 4 seconds";
        var outcome = await printer.PrintTestLabelToAsync("Network", "10.0.0.9:9100", "Bar");

        Assert.False(outcome.Printed);
        Assert.Equal(PrinterState.Unreachable, outcome.Condition.State);

        // The host's own printer is untouched, and no screen was told to re-render.
        Assert.Equal(PrinterState.Ready, printer.Condition.State);
        Assert.Equal("Counter", printer.Selected?.Name);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task A_transport_this_host_does_not_have_is_said_rather_than_dialled()
    {
        // The production case the deployment decision creates: the back office is a
        // browser pointed at a central server, the venue owns a Bluetooth printer, and
        // that server has no radio near it. Nothing is dialled, so nothing is reported
        // about the printer — the sentence is about the host.
        var transport = new RecordingTransport("Network");
        var printer = new TransportReceiptPrinter(transport, new InMemoryPrinterPreference());

        var outcome = await printer.PrintTestLabelToAsync("Bluetooth", "00:11:62:4C:58:5D", "Bar");

        Assert.False(outcome.Printed);
        Assert.Equal(PrinterState.Failed, outcome.Condition.State);
        Assert.NotNull(outcome.Condition.Message);
        Assert.Contains("Bluetooth", outcome.Condition.Message);
        Assert.Contains('·', outcome.Condition.Message!);
        Assert.Null(transport.ConnectedTo);
    }

    [Fact]
    public async Task A_row_with_no_address_is_refused_before_anything_is_dialled()
    {
        var transport = new RecordingTransport("Network");
        var printer = new TransportReceiptPrinter(transport, new InMemoryPrinterPreference());

        var outcome = await printer.PrintTestLabelToAsync("Network", "   ", "Bar");

        Assert.False(outcome.Printed);
        Assert.NotNull(outcome.Condition.Message);
        Assert.Contains('·', outcome.Condition.Message!);
        Assert.Null(transport.ConnectedTo);
    }

    [Fact]
    public async Task A_paper_fault_at_the_registry_printer_is_reported_to_the_caller_only()
    {
        // The printer answered and said it cannot print. That is a real fault and the
        // outcome carries it — but it is the bar printer's fault, not this host's, so
        // the host's condition still says what it said.
        // A clean Star Line ASB block with the paper-empty bit set, the same fixture
        // StarLineStatusTests builds.
        var block = new byte[] { 0x20, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40 };
        block[4] |= 0x08;

        var transport = new RecordingTransport("Network") { Reply = block };
        var printer = new TransportReceiptPrinter(transport, new InMemoryPrinterPreference());

        var outcome = await printer.PrintTestLabelToAsync("Network", "192.168.1.50:9100", "Bar");

        Assert.False(outcome.Printed);
        Assert.Equal(PrinterState.PaperOut, outcome.Condition.State);

        // The bar printer has no paper. Nothing about this host changed, so its own
        // condition still reads what it read.
        Assert.Equal(PrinterState.NoPrinter, printer.Condition.State);
    }

    [Fact]
    public async Task A_registry_print_and_a_host_print_do_not_share_the_socket()
    {
        // Two writes overlapping down one socket interleave into a label carrying half
        // of each. The gate does not care why the two jobs exist, and this proves the
        // new path went through it rather than around it.
        var transport = new RecordingTransport("Network") { WriteDelay = TimeSpan.FromMilliseconds(60) };
        var printer = new TransportReceiptPrinter(transport, new InMemoryPrinterPreference());

        await printer.SelectAsync(new PrinterDevice("192.168.1.50:9100", "Counter"));

        var host = printer.PrintTestLabelAsync("Counter 2");
        var registry = printer.PrintTestLabelToAsync("Network", "10.0.0.9:9100", "Bar");

        await Task.WhenAll(host, registry);

        Assert.False(transport.Overlapped);
        Assert.Equal(2, transport.Connects);
    }

    [Fact]
    public async Task A_host_with_no_transport_refuses_a_registry_print_honestly()
    {
        var printer = new UnavailableReceiptPrinter();

        var outcome = await printer.PrintTestLabelToAsync("Network", "192.168.1.50:9100", "Bar");

        Assert.False(outcome.Printed);
        Assert.Equal(PrinterState.NoPrinter, outcome.Condition.State);
        Assert.Contains("no printer", outcome.Condition.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A transport made of nothing that records what it was asked to do. Named, so the
    /// routing assertions have something to route to.
    /// </summary>
    private sealed class RecordingTransport(string name) : IPrinterTransport
    {
        public List<byte[]> Writes { get; } = new();

        public byte[] Reply { get; set; } = Array.Empty<byte>();

        public string? ConnectFailure { get; set; }

        public TimeSpan WriteDelay { get; set; }

        public string? ConnectedTo { get; private set; }

        public int Connects;

        public int Disposals;

        public bool Overlapped;

        private int _open;

        public string Name => name;

        public bool IsAvailable(out string? reason)
        {
            reason = null;
            return true;
        }

        public Task<IReadOnlyList<PrinterDevice>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PrinterDevice>>(Array.Empty<PrinterDevice>());

        public Task<IPrinterConnection> ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (ConnectFailure is not null)
            {
                throw new IOException(ConnectFailure);
            }

            ConnectedTo = deviceId;
            Connects++;

            if (Interlocked.Increment(ref _open) > 1)
            {
                Overlapped = true;
            }

            return Task.FromResult<IPrinterConnection>(new Connection(this));
        }

        private sealed class Connection(RecordingTransport owner) : IPrinterConnection
        {
            public async Task WriteAsync(byte[] payload, CancellationToken cancellationToken = default)
            {
                if (owner.WriteDelay > TimeSpan.Zero)
                {
                    await Task.Delay(owner.WriteDelay, cancellationToken);
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
