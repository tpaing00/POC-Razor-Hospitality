using System.Net;
using Restaurant.UI.Shared.Services.Printing.Network;
using Xunit;

namespace Restaurant.Printing.Tests;

/// <summary>
/// The two parts of network discovery that are decisions rather than sockets: what an
/// address a person typed is allowed to be, and how far a subnet probe is allowed to
/// reach.
///
/// The bounds on the probe are the ones worth holding a test over. A restaurant
/// network is not a lab: a sweep that widens by one bit goes from 254 hosts to 510, a
/// /16 misread as sweepable is 65,534, and the difference between a bounded discovery
/// and a port scan is entirely in these numbers.
/// </summary>
public class NetworkDiscoveryTests
{
    // ─── An address somebody typed ───────────────────────────────────────────

    [Theory]
    [InlineData("192.168.1.50", "192.168.1.50", 9100)]
    [InlineData("  192.168.1.50  ", "192.168.1.50", 9100)]
    [InlineData("192.168.1.50:9100", "192.168.1.50", 9100)]
    [InlineData("192.168.1.50:515", "192.168.1.50", 515)]
    [InlineData("printer.local", "printer.local", 9100)]
    [InlineData("star-tsp143iv", "star-tsp143iv", 9100)]
    [InlineData("http://192.168.1.50/", "192.168.1.50", 9100)]
    [InlineData("tcp://192.168.1.50:9100", "192.168.1.50", 9100)]
    [InlineData("[fe80::1]:9100", "fe80::1", 9100)]
    [InlineData("fe80::1", "fe80::1", 9100)]
    public void An_address_parses_to_a_host_and_a_port(string typed, string host, int port)
    {
        var parsed = TcpPrinterTransport.ParseAddress(typed);

        Assert.Equal(host, parsed.Host);
        Assert.Equal(port, parsed.Port);
    }

    [Fact]
    public void A_bare_address_defaults_to_the_port_star_listens_on()
    {
        // 9100 is raw printing on every Star unit with a network interface. Nobody
        // should have to know that to type an address.
        Assert.Equal(9100, TcpPrinterTransport.DefaultPort);
        Assert.Equal(9100, TcpPrinterTransport.ParseAddress("192.168.1.50").Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("192.168.1.50:0")]
    [InlineData("192.168.1.50:99999")]
    [InlineData("192.168.1.50:printer")]
    [InlineData("what is the printer called")]
    [InlineData("[fe80::1")]
    public void An_address_that_will_not_parse_says_what_was_expected(string typed)
    {
        var error = Assert.Throws<FormatException>(() => TcpPrinterTransport.ParseAddress(typed));

        // §10: the cause and the next move. "Invalid input" tells a person standing in
        // front of a printer nothing they can act on, so every one of these carries an
        // example of what would have worked.
        Assert.NotEmpty(error.Message);
        Assert.Contains("·", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_typed_address_is_recorded_without_dialling_anything()
    {
        // The printer is switched off at half past four and has the same address at
        // five. Refusing to record it because nothing answered would be the screen
        // deciding a fault it has not diagnosed.
        var transport = new TcpPrinterTransport();

        var device = await transport.ResolveAsync("192.0.2.1");

        Assert.Equal("192.0.2.1:9100", device.Id);
        Assert.False(device.IsPaired);
    }

    // ─── How far the probe reaches ───────────────────────────────────────────

    [Fact]
    public void A_slash_24_yields_every_host_but_the_network_the_broadcast_and_this_one()
    {
        var candidates = SubnetProbe.Candidates(IPAddress.Parse("192.168.1.10"), 24);

        Assert.Equal(253, candidates.Count);
        Assert.DoesNotContain(candidates, a => a.ToString() == "192.168.1.0");
        Assert.DoesNotContain(candidates, a => a.ToString() == "192.168.1.255");
        Assert.DoesNotContain(candidates, a => a.ToString() == "192.168.1.10");
        Assert.Contains(candidates, a => a.ToString() == "192.168.1.1");
        Assert.Contains(candidates, a => a.ToString() == "192.168.1.254");
    }

    [Theory]
    [InlineData(23)]
    [InlineData(16)]
    [InlineData(8)]
    public void A_subnet_wider_than_a_slash_24_is_refused_rather_than_truncated(int prefix)
    {
        // Refused, and this is the assertion the whole probe design rests on. A /16 is
        // 65,534 hosts and there is no timeout short enough to make sweeping it
        // acceptable on a wire carrying card terminals. Truncating instead would be
        // worse than refusing: it would sweep the first 254 addresses, none of which is
        // the printer, and report nothing found.
        Assert.Empty(SubnetProbe.Candidates(IPAddress.Parse("10.1.2.3"), prefix));
        Assert.Equal(24, SubnetProbe.MinimumPrefixLength);
    }

    [Fact]
    public void A_narrower_subnet_is_swept_and_is_smaller()
    {
        // A /26 is 62 hosts. Narrower is fine — the bound is on how wide it can be.
        var candidates = SubnetProbe.Candidates(IPAddress.Parse("192.168.1.70"), 26);

        Assert.Equal(61, candidates.Count);
        Assert.Contains(candidates, a => a.ToString() == "192.168.1.65");
        Assert.Contains(candidates, a => a.ToString() == "192.168.1.126");
        Assert.DoesNotContain(candidates, a => a.ToString() == "192.168.1.64");
        Assert.DoesNotContain(candidates, a => a.ToString() == "192.168.1.127");
        Assert.DoesNotContain(candidates, a => a.ToString() == "192.168.1.128");
    }

    [Fact]
    public void An_ipv6_address_is_never_swept()
    {
        // There is no bounded sweep of an IPv6 subnet, and pretending otherwise would
        // produce a discovery that does not end. The manual address field takes an
        // IPv6 literal, which is the honest answer.
        Assert.Empty(SubnetProbe.Candidates(IPAddress.Parse("fe80::1"), 64));
    }

    [Fact]
    public void The_sweep_is_capped_in_hosts_and_in_connections_at_once()
    {
        // Named constants rather than magic numbers inside the loop, so the bound is a
        // thing a reviewer can find. 24 at a time rather than 254: a burst of 254
        // simultaneous connects is what a switch reads as a scan.
        Assert.Equal(254, SubnetProbe.MaxHosts);
        Assert.Equal(24, SubnetProbe.MaxConcurrent);
        Assert.True(SubnetProbe.ConnectTimeout <= TimeSpan.FromMilliseconds(500));
        Assert.All(
            SubnetProbe.Candidates(IPAddress.Parse("192.168.1.10"), 24),
            _ => { });
        Assert.True(SubnetProbe.Candidates(IPAddress.Parse("192.168.1.10"), 24).Count <= SubnetProbe.MaxHosts);
    }

    [Fact]
    public async Task A_sweep_of_addresses_nothing_is_listening_on_returns_empty_and_ends()
    {
        // 192.0.2.0/24 is TEST-NET-1: reserved by RFC 5737 for documentation, routed
        // nowhere, and therefore safe to point a probe at from a test. What is being
        // asserted is that the sweep ends inside its own bounds rather than hanging.
        var addresses = SubnetProbe.Candidates(IPAddress.Parse("192.0.2.1"), 29);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var answered = await SubnetProbe.ScanAsync(addresses, cancellation.Token);

        Assert.Empty(answered);
    }

    // ─── mDNS is asked for the services a Star unit advertises ───────────────

    [Fact]
    public void The_raw_printing_service_is_asked_for_first()
    {
        // _pdl-datastream is the one that carries port 9100 in its SRV and is directly
        // usable. The other two are asked for because a unit that advertises only those
        // still tells us its address, and 9100 is open on a Star unit whether or not it
        // says so.
        Assert.Equal("_pdl-datastream._tcp.local", MdnsBrowser.PrinterServices[0]);
        Assert.Contains("_printer._tcp.local", MdnsBrowser.PrinterServices);
        Assert.Contains("_ipp._tcp.local", MdnsBrowser.PrinterServices);
    }

    [Fact]
    public void The_listening_window_is_short_enough_that_a_person_waits_through_it()
    {
        Assert.True(MdnsBrowser.Window <= TimeSpan.FromSeconds(3));
        Assert.True(MdnsBrowser.Window >= TimeSpan.FromSeconds(1));
    }
}
