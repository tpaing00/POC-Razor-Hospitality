using Restaurant.Shared.Models;
using Xunit;

namespace Restaurant.Printing.Tests;

/// <summary>
/// What the venue's registry will store as an address, and what it refuses.
///
/// **This is the guard on a record other machines read.** A row holding
/// <c>my printer</c> is not a printer that is switched off; it is a row nothing can
/// ever connect to, sitting in the venue's list looking exactly like a working one
/// until somebody walks over and fires a test label at it. None of this needs a
/// database, a network or a printer — it is a pure function in both directions.
///
/// The normalization half matters as much as the refusal half: the unique index on
/// (Transport, Address) is only true if <c>192.168.1.50</c> and
/// <c>192.168.1.50:9100</c> collide, and they only collide if they are stored the same.
/// </summary>
public class PrinterAddressTests
{
    // ─── Network · what is accepted, and what it becomes ─────────────────────

    [Theory]
    [InlineData("192.168.1.50", "192.168.1.50:9100")]
    [InlineData("192.168.1.50:9100", "192.168.1.50:9100")]
    [InlineData("192.168.1.50:9101", "192.168.1.50:9101")]
    [InlineData("  192.168.1.50  ", "192.168.1.50:9100")]
    [InlineData("printer.local", "printer.local:9100")]
    [InlineData("Printer.Local", "printer.local:9100")]
    [InlineData("[fe80::1]", "[fe80::1]:9100")]
    [InlineData("[fe80::1]:9100", "[fe80::1]:9100")]
    [InlineData("fe80::1", "[fe80::1]:9100")]
    public void A_network_address_is_stored_in_one_form(string typed, string stored)
    {
        Assert.True(PrinterAddress.TryNormalize(PrinterTransportKind.Network, typed, out var normalized, out var error));
        Assert.Null(error);
        Assert.Equal(stored, normalized);
    }

    [Fact]
    public void The_port_defaults_to_the_raw_printing_port()
    {
        // 9100 is raw print data by definition, which is why Star exposes it and why a
        // print job needs no protocol on top. A person reading an address off a
        // self-test page reads the address, not the port.
        Assert.Equal(9100, PrinterAddress.DefaultRawPrintingPort);

        PrinterAddress.TryNormalize(PrinterTransportKind.Network, "10.0.0.7", out var normalized, out _);
        Assert.Equal($"10.0.0.7:{PrinterAddress.DefaultRawPrintingPort}", normalized);
    }

    [Fact]
    public void A_pasted_configuration_page_url_is_read_as_the_address_in_it()
    {
        // The printer's own web page is the most likely place somebody copies its
        // address from. The scheme's port is deliberately NOT kept: the web page is on
        // 80 and the print port is 9100, so honouring the scheme would store the wrong
        // number and every test label would fail against a printer that was fine.
        Assert.True(PrinterAddress.TryNormalize(
            PrinterTransportKind.Network, "http://192.168.1.50/config", out var normalized, out _));

        Assert.Equal("192.168.1.50:9100", normalized);
    }

    [Fact]
    public void Credentials_in_a_pasted_url_are_dropped_rather_than_stored()
    {
        // A pasted URL can carry the printer's admin login. It is not part of an
        // address a printer is dialled at, and keeping it would put a password in the
        // venue's registry where every reader of the API can see it.
        Assert.True(PrinterAddress.TryNormalize(
            PrinterTransportKind.Network, "http://admin:letmein@192.168.1.50/", out var normalized, out _));

        Assert.Equal("192.168.1.50:9100", normalized);
        Assert.DoesNotContain("letmein", normalized);
    }

    // ─── Network · what is refused ───────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a printer address")]
    [InlineData("192.168.1.50:")]
    [InlineData("192.168.1.50:0")]
    [InlineData("192.168.1.50:70000")]
    [InlineData("192.168.1.50:nine")]
    [InlineData("192.168.1.50:-1")]
    [InlineData("[fe80::1")]
    public void A_network_address_that_will_not_parse_is_refused_with_a_sentence(string typed)
    {
        Assert.False(PrinterAddress.TryNormalize(PrinterTransportKind.Network, typed, out var normalized, out var error));
        Assert.Null(normalized);

        // §10: one line, the cause and the next move, middot-separated. A refusal a
        // person cannot act on is a refusal that gets retyped identically.
        Assert.Contains('·', error);
    }

    [Fact]
    public void An_address_longer_than_the_column_is_refused_rather_than_truncated()
    {
        // Truncating would store a different address from the one that was typed and
        // report success, which is the worst of the three outcomes.
        var tooLong = new string('a', PrinterAddress.MaxLength + 1);

        Assert.False(PrinterAddress.TryNormalize(PrinterTransportKind.Network, tooLong, out _, out var error));
        Assert.Contains(PrinterAddress.MaxLength.ToString(), error);
    }

    [Fact]
    public void Nothing_here_opens_a_socket()
    {
        // TEST-NET-1, reserved by RFC 5737 and routable nowhere. It is accepted, and
        // accepted instantly: a printer switched off at half past four has the same
        // address at five, and refusing to record it because nothing answered would be
        // the server deciding a fault it has not diagnosed.
        var started = DateTime.UtcNow;

        Assert.True(PrinterAddress.TryNormalize(PrinterTransportKind.Network, "192.0.2.1", out var normalized, out _));

        Assert.Equal("192.0.2.1:9100", normalized);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromMilliseconds(500));
    }

    // ─── Bluetooth ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("00:11:62:4C:58:5D")]
    [InlineData("00:11:62:4c:58:5d")]
    [InlineData("00-11-62-4C-58-5D")]
    [InlineData("0011624C585D")]
    [InlineData(" 00:11:62:4C:58:5D ")]
    public void A_bluetooth_address_is_stored_in_one_form(string typed)
    {
        // Colons, hyphens and nothing between the pairs are all printed on real
        // hardware and shown on real screens. What comes back is always the form the
        // Windows and Android stacks both display.
        Assert.True(PrinterAddress.TryNormalize(PrinterTransportKind.Bluetooth, typed, out var normalized, out var error));
        Assert.Null(error);
        Assert.Equal("00:11:62:4C:58:5D", normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("00:11:62:4C:58")]
    [InlineData("00:11:62:4C:58:5D:6E")]
    [InlineData("ZZ:11:62:4C:58:5D")]
    [InlineData("192.168.1.50")]
    public void A_bluetooth_address_that_is_not_six_pairs_is_refused_with_a_sentence(string typed)
    {
        Assert.False(PrinterAddress.TryNormalize(PrinterTransportKind.Bluetooth, typed, out var normalized, out var error));
        Assert.Null(normalized);
        Assert.Contains('·', error);
    }

    [Fact]
    public void The_two_transports_do_not_accept_each_others_addresses()
    {
        // The whole reason Transport is stored beside Address. A MAC in a network row
        // would be dialled as a host name, and an IP in a Bluetooth row would be looked
        // up in a bond list that has never heard of it. Both fail late and confusingly;
        // this fails immediately and says which field is wrong.
        Assert.False(PrinterAddress.TryNormalize(PrinterTransportKind.Network, "00:11:62:4C:58:5D", out _, out _));
        Assert.False(PrinterAddress.TryNormalize(PrinterTransportKind.Bluetooth, "192.168.1.50:9100", out _, out _));
    }

    [Fact]
    public void A_transport_this_build_cannot_reach_is_refused_by_name()
    {
        // An enum value cast from a number nobody defined. It reaches here from a
        // client that sent 7, and the answer names what the build does have rather
        // than throwing.
        Assert.False(PrinterAddress.TryNormalize((PrinterTransportKind)7, "192.168.1.50", out _, out var error));
        Assert.Contains("Network", error);
        Assert.Contains("Bluetooth", error);
    }
}
