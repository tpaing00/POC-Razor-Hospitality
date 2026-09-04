using Restaurant.UI.Shared.Services.Printing;
using Xunit;

namespace Restaurant.Printing.Tests;

/// <summary>
/// Several transports behaving as one, driven by transports made of nothing.
///
/// This is where the multi-transport bugs live and none of them need hardware to
/// catch: a device routed to the wrong transport, a remembered pairing that stops
/// resolving after a restart, one transport's failure taking another's results down
/// with it, and — the one this feature turns on — a host with no Bluetooth radio being
/// rendered as a host that found no printers.
/// </summary>
public class CompositePrinterTransportTests
{
    private static PrinterDevice Bluetooth => new("00:11:22:33:44:55", "Star TSP143IV");

    private static PrinterDevice Network => new("192.168.1.50:9100", "Star TSP143IV (network)");

    // ─── Availability ────────────────────────────────────────────────────────

    [Fact]
    public void One_member_working_is_enough()
    {
        // A back office on a desktop with no Bluetooth radio still prints over the
        // network. Reporting the whole thing unavailable would take the working half
        // off the screen.
        var composite = new CompositePrinterTransport(new IPrinterTransport[]
        {
            new FakeNamedTransport("Network"),
            new FakeNamedTransport("Bluetooth", available: false, reason: "no radio on this host")
        });

        Assert.True(composite.IsAvailable(out var reason));
        Assert.Null(reason);
    }

    [Fact]
    public void The_missing_radio_is_still_reported_while_the_network_works()
    {
        // THE ASSERTION THIS WHOLE FEATURE TURNS ON. "This host has no Bluetooth radio"
        // and "no printers found" are different facts, and a screen with only
        // IsAvailable would have nowhere to put the first. Describe is where it goes,
        // and it survives the composite being available.
        var composite = new CompositePrinterTransport(new IPrinterTransport[]
        {
            new FakeNamedTransport("Network"),
            new FakeNamedTransport("Bluetooth", available: false, reason: "no radio on this host")
        });

        var rows = composite.Describe();

        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].Available);
        Assert.Null(rows[0].Reason);
        Assert.Equal("Bluetooth", rows[1].Name);
        Assert.False(rows[1].Available);
        Assert.Equal("no radio on this host", rows[1].Reason);
    }

    [Fact]
    public void When_nothing_works_every_reason_is_carried()
    {
        var composite = new CompositePrinterTransport(new IPrinterTransport[]
        {
            new FakeNamedTransport("Network", available: false, reason: "not on a network"),
            new FakeNamedTransport("Bluetooth", available: false, reason: "no radio on this host")
        });

        Assert.False(composite.IsAvailable(out var reason));
        Assert.Contains("not on a network", reason!, StringComparison.Ordinal);
        Assert.Contains("no radio on this host", reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_host_with_no_transports_says_so_rather_than_finding_nothing()
    {
        var composite = new CompositePrinterTransport(Array.Empty<IPrinterTransport>());

        Assert.False(composite.IsAvailable(out var reason));
        Assert.NotNull(reason);
        Assert.Empty(composite.Describe());
    }

    [Fact]
    public void Describing_a_nested_composite_reports_its_leaves()
    {
        var inner = new CompositePrinterTransport(new IPrinterTransport[]
        {
            new FakeNamedTransport("Network"),
            new FakeNamedTransport("Bluetooth", available: false, reason: "no radio")
        });

        var outer = new CompositePrinterTransport(new IPrinterTransport[] { inner });

        Assert.Equal(new[] { "Network", "Bluetooth" }, outer.Describe().Select(r => r.Name));
    }

    // ─── Discovery ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Every_member_is_asked_and_every_result_is_labelled_with_its_transport()
    {
        var composite = new CompositePrinterTransport(new IPrinterTransport[]
        {
            new FakeNamedTransport("Network") { Devices = { Network } },
            new FakeNamedTransport("Bluetooth") { Devices = { Bluetooth } }
        });

        var found = await composite.DiscoverAsync();

        Assert.Equal(2, found.Count);
        Assert.Equal("Network", found[0].Transport);
        Assert.Equal("Bluetooth", found[1].Transport);
    }

    [Fact]
    public async Task Registration_order_is_the_order_a_person_reads_them_in()
    {
        // The list must not reshuffle between scans because two devices answered in a
        // different order — a list nobody can point at is a list nobody trusts.
        var composite = new CompositePrinterTransport(new IPrinterTransport[]
        {
            new FakeNamedTransport("Network") { Devices = { new PrinterDevice("2", "Zebra"), new PrinterDevice("1", "Alpha") } },
            new FakeNamedTransport("Bluetooth") { Devices = { new PrinterDevice("3", "Beta") } }
        });

        var found = await composite.DiscoverAsync();

        Assert.Equal(new[] { "Alpha", "Zebra", "Beta" }, found.Select(d => d.Name));
    }

    [Fact]
    public async Task A_member_that_throws_does_not_take_the_other_members_results_down()
    {
        // A Bluetooth stack that raises on enumeration must not hide two printers the
        // network found.
        var composite = new CompositePrinterTransport(new IPrinterTransport[]
        {
            new FakeNamedTransport("Network") { Devices = { Network } },
            new FakeNamedTransport("Bluetooth") { Throws = true }
        });

        var found = await composite.DiscoverAsync();

        Assert.Single(found);
        Assert.Equal("Network", found[0].Transport);
    }

    [Fact]
    public async Task A_member_that_says_it_is_unavailable_is_never_dialled()
    {
        // Asking a transport that has already said it has no radio is how a screen ends
        // up rendering a stack trace instead of a sentence.
        var bluetooth = new FakeNamedTransport("Bluetooth", available: false, reason: "no radio")
        {
            Devices = { Bluetooth }
        };

        var composite = new CompositePrinterTransport(new IPrinterTransport[]
        {
            new FakeNamedTransport("Network") { Devices = { Network } },
            bluetooth
        });

        var found = await composite.DiscoverAsync();

        Assert.Single(found);
        Assert.False(bluetooth.WasDiscovered);
    }

    [Fact]
    public async Task Members_run_at_the_same_time_rather_than_one_after_the_other()
    {
        // An mDNS window is two and a half seconds and a Bluetooth inquiry is twelve.
        // Run in sequence, a person waits for the sum of two clocks that could have run
        // together.
        var composite = new CompositePrinterTransport(new IPrinterTransport[]
        {
            new FakeNamedTransport("Network") { Delay = TimeSpan.FromMilliseconds(300) },
            new FakeNamedTransport("Bluetooth") { Delay = TimeSpan.FromMilliseconds(300) }
        });

        var started = DateTime.UtcNow;
        await composite.DiscoverAsync();
        var elapsed = DateTime.UtcNow - started;

        Assert.True(elapsed < TimeSpan.FromMilliseconds(550), $"took {elapsed.TotalMilliseconds:0}ms");
    }

    // ─── Routing ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_routed_id_carries_the_transport_and_the_address_stays_showable()
    {
        var composite = new CompositePrinterTransport(new IPrinterTransport[]
        {
            new FakeNamedTransport("Bluetooth") { Devices = { Bluetooth } }
        });

        var device = Assert.Single(await composite.DiscoverAsync());

        // The prefix routes the connection; a MAC address has six colons and no slash,
        // so the separator cannot collide with either kind of address.
        Assert.Equal("bluetooth/00:11:22:33:44:55", device.Id);

        // And no screen ever has to show the routing key.
        Assert.Equal("00:11:22:33:44:55", device.Display);
    }

    [Fact]
    public async Task Connecting_reaches_the_transport_that_found_it_with_its_own_id()
    {
        var network = new FakeNamedTransport("Network") { Devices = { Network } };
        var bluetooth = new FakeNamedTransport("Bluetooth") { Devices = { Bluetooth } };
        var composite = new CompositePrinterTransport(new IPrinterTransport[] { network, bluetooth });

        await composite.ConnectAsync("bluetooth/00:11:22:33:44:55");

        Assert.Equal("00:11:22:33:44:55", bluetooth.ConnectedTo);
        Assert.Null(network.ConnectedTo);
    }

    [Fact]
    public async Task A_remembered_pairing_still_routes_after_a_restart()
    {
        // The preference remembers an id and nothing else, so the id is the only thing
        // that survives. A key derived from the transport's own name and its
        // registration order is stable across restarts, which is what makes that work.
        var first = new CompositePrinterTransport(new IPrinterTransport[]
        {
            new FakeNamedTransport("Network") { Devices = { Network } },
            new FakeNamedTransport("Bluetooth") { Devices = { Bluetooth } }
        });

        var remembered = (await first.DiscoverAsync()).Single(d => d.Transport == "Network").Id;

        var restarted = new FakeNamedTransport("Network");
        var second = new CompositePrinterTransport(new IPrinterTransport[]
        {
            restarted,
            new FakeNamedTransport("Bluetooth")
        });

        await second.ConnectAsync(remembered);

        Assert.Equal("192.168.1.50:9100", restarted.ConnectedTo);
    }

    [Fact]
    public async Task An_id_no_transport_here_owns_says_what_to_do_instead()
    {
        // A pairing remembered on a host that had a different set of transports. It
        // happens, and it has to say so rather than throw a null.
        var composite = new CompositePrinterTransport(new IPrinterTransport[]
        {
            new FakeNamedTransport("Network")
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => composite.ConnectAsync("bluetooth/00:11:22:33:44:55"));

        Assert.Contains("·", error.Message, StringComparison.Ordinal);

        var bare = await Assert.ThrowsAsync<InvalidOperationException>(
            () => composite.ConnectAsync("00:11:22:33:44:55"));

        Assert.Contains("·", bare.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_transports_with_the_same_name_stay_apart()
    {
        var first = new FakeNamedTransport("Network") { Devices = { new PrinterDevice("a", "First") } };
        var second = new FakeNamedTransport("Network") { Devices = { new PrinterDevice("b", "Second") } };
        var composite = new CompositePrinterTransport(new IPrinterTransport[] { first, second });

        var found = await composite.DiscoverAsync();

        Assert.Equal(new[] { "network/a", "network2/b" }, found.Select(d => d.Id));

        await composite.ConnectAsync("network2/b");
        Assert.Equal("b", second.ConnectedTo);
        Assert.Null(first.ConnectedTo);
    }

    // ─── An address somebody typed ───────────────────────────────────────────

    [Fact]
    public async Task The_first_addressable_member_takes_a_typed_address_and_the_result_is_routed()
    {
        var composite = new CompositePrinterTransport(new IPrinterTransport[]
        {
            new FakeNamedTransport("Bluetooth"),
            new FakeAddressableTransport("Network")
        });

        Assert.True(composite.AcceptsAddress);

        var device = await composite.ResolveAsync("192.168.1.50");

        Assert.Equal("network/192.168.1.50:9100", device.Id);
        Assert.Equal("Network", device.Transport);
        Assert.Equal("192.168.1.50:9100", device.Display);
    }

    [Fact]
    public void A_composite_with_no_addressable_member_does_not_offer_the_field()
    {
        // A composite is available when any member is, which is not the same as any
        // member taking an address. The terminal's Bluetooth transport takes none, and
        // the screen must not draw a field that goes nowhere.
        var composite = new CompositePrinterTransport(new IPrinterTransport[]
        {
            new FakeNamedTransport("Bluetooth")
        });

        Assert.True(composite.IsAvailable(out _));
        Assert.False(composite.AcceptsAddress);
    }

    [Fact]
    public async Task The_printer_reports_the_composite_rows_and_the_address_field_through_the_state_machine()
    {
        // The state machine over a composite: unchanged in every other respect, and
        // reporting the two facts a multi-transport host needs.
        var composite = new CompositePrinterTransport(new IPrinterTransport[]
        {
            new FakeAddressableTransport("Network"),
            new FakeNamedTransport("Bluetooth", available: false, reason: "no radio on this host")
        });

        var printer = new TransportReceiptPrinter(composite, new InMemoryPrinterPreference());

        Assert.True(printer.IsSupported);
        Assert.True(printer.SupportsAddressEntry);
        Assert.Equal(2, printer.Transports.Count);
        Assert.False(printer.Transports[1].Available);

        await printer.AddByAddressAsync("192.168.1.50");

        // Typed, selected, and NOT TESTED — because typing an address proves the
        // address is well formed and nothing else.
        Assert.Equal("network/192.168.1.50:9100", printer.Selected!.Id);
        Assert.Equal(PrinterState.NotTested, printer.Condition.State);
        Assert.Contains(printer.Found, d => d.Id == printer.Selected.Id);
    }

    [Fact]
    public async Task An_address_that_will_not_parse_leaves_the_transports_own_sentence_on_screen()
    {
        var composite = new CompositePrinterTransport(new IPrinterTransport[]
        {
            new FakeAddressableTransport("Network")
        });

        var printer = new TransportReceiptPrinter(composite, new InMemoryPrinterPreference());

        await printer.AddByAddressAsync("not an address");

        Assert.Equal(PrinterState.Failed, printer.Condition.State);
        Assert.Contains("not an address", printer.Condition.Message!, StringComparison.Ordinal);
        Assert.Null(printer.Selected);
    }

    [Fact]
    public async Task A_printer_over_a_single_transport_reports_one_row_and_no_address_field()
    {
        // The terminal's arrangement, unchanged. One transport, one row, no field —
        // which is why the screen renders neither block there.
        var printer = new TransportReceiptPrinter(
            new FakeNamedTransport("Bluetooth"),
            new InMemoryPrinterPreference());

        var row = Assert.Single(printer.Transports);
        Assert.Equal("Bluetooth", row.Name);
        Assert.True(row.Available);
        Assert.False(printer.SupportsAddressEntry);

        await printer.AddByAddressAsync("192.168.1.50");

        Assert.Null(printer.Selected);
        Assert.Equal(PrinterState.Failed, printer.Condition.State);
    }

    // ─── Transports made of nothing ──────────────────────────────────────────

    private class FakeNamedTransport(string name, bool available = true, string? reason = null)
        : IPrinterTransport
    {
        public List<PrinterDevice> Devices { get; } = new();

        public bool Throws { get; set; }

        public TimeSpan Delay { get; set; }

        public bool WasDiscovered { get; private set; }

        public string? ConnectedTo { get; private set; }

        public string Name => name;

        public bool IsAvailable(out string? why)
        {
            why = available ? null : reason;
            return available;
        }

        public async Task<IReadOnlyList<PrinterDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            WasDiscovered = true;

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            if (Throws)
            {
                throw new InvalidOperationException("this transport is broken");
            }

            return Devices;
        }

        public Task<IPrinterConnection> ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            ConnectedTo = deviceId;
            return Task.FromResult<IPrinterConnection>(new NullConnection());
        }
    }

    private sealed class FakeAddressableTransport(string name)
        : FakeNamedTransport(name), IAddressablePrinterTransport
    {
        public Task<PrinterDevice> ResolveAsync(string address, CancellationToken cancellationToken = default)
        {
            if (!address.Contains('.') || address.Contains(' '))
            {
                throw new FormatException($"“{address}” is not an address · try 192.168.1.50");
            }

            var id = address.Contains(':') ? address : $"{address}:9100";
            return Task.FromResult(new PrinterDevice(id, id, IsPaired: false));
        }
    }

    private sealed class NullConnection : IPrinterConnection
    {
        public Task WriteAsync(byte[] payload, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<byte[]> ReadAsync(TimeSpan wait, CancellationToken cancellationToken = default) =>
            Task.FromResult(Array.Empty<byte>());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
