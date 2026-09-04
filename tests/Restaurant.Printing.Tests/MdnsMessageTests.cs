using System.Text;
using Restaurant.UI.Shared.Services.Printing.Network;
using Xunit;

namespace Restaurant.Printing.Tests;

/// <summary>
/// The multicast DNS packet, byte for byte.
///
/// This is the network transport's framing, and it is the half of network discovery
/// that can be got wrong silently: a query with a malformed question is a query no
/// responder answers, and a parser that does not follow a compression pointer reads a
/// printer's name as two characters of rubbish and finds nothing. Neither failure
/// raises anything. Both look exactly like "there is no printer on this network",
/// which is the sentence a person would then act on for an hour.
///
/// So the packets here are built by hand, with the same compression a real responder
/// uses, and compared against what the parser makes of them.
/// </summary>
public class MdnsMessageTests
{
    // ─── The query ───────────────────────────────────────────────────────────

    [Fact]
    public void The_query_is_one_packet_carrying_every_service()
    {
        var query = MdnsMessage.BuildQuery(MdnsBrowser.PrinterServices);

        // Header: id 0, flags 0, then the question count. Three questions in one
        // packet rather than three packets, which is the whole point — a restaurant
        // network carries card terminals on the same wire.
        Assert.Equal(0, query[0]);
        Assert.Equal(0, query[1]);
        Assert.Equal(0, query[2]);
        Assert.Equal(0, query[3]);
        Assert.Equal(0, query[4]);
        Assert.Equal(3, query[5]);

        // Nothing but questions.
        Assert.Equal(0, query[6] + query[7] + query[8] + query[9] + query[10] + query[11]);
    }

    [Fact]
    public void A_question_is_labels_a_terminator_then_ptr_and_in()
    {
        var query = MdnsMessage.BuildQuery(new[] { "_pdl-datastream._tcp.local" });

        var expected = new List<byte> { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (var label in new[] { "_pdl-datastream", "_tcp", "local" })
        {
            expected.Add((byte)label.Length);
            expected.AddRange(Encoding.UTF8.GetBytes(label));
        }

        expected.Add(0);        // end of name
        expected.Add(0);        // QTYPE PTR
        expected.Add(12);
        expected.Add(0);        // QCLASS IN
        expected.Add(1);

        Assert.Equal(expected.ToArray(), query);
    }

    [Fact]
    public void The_unicast_bit_is_the_top_bit_of_the_class_and_nothing_else_moves()
    {
        var multicast = MdnsMessage.BuildQuery(new[] { "_ipp._tcp.local" });
        var unicast = MdnsMessage.BuildQuery(new[] { "_ipp._tcp.local" }, unicastResponse: true);

        Assert.Equal(multicast.Length, unicast.Length);
        Assert.Equal(multicast[^1], unicast[^1]);

        // 0x0001 becomes 0x8001. The fallback path sets it when the socket could not
        // take port 5353, and setting anything else would make responders ignore the
        // question entirely.
        Assert.Equal(0x00, multicast[^2]);
        Assert.Equal(0x80, unicast[^2]);
    }

    // ─── The answer ──────────────────────────────────────────────────────────

    [Fact]
    public void A_pointer_srv_and_address_resolve_to_one_printer()
    {
        var packet = StarResponse("Star TSP143IV", "192.168.1.50", 9100);

        var printers = MdnsMessage.Printers(MdnsMessage.Parse(packet));

        var printer = Assert.Single(printers);
        Assert.Equal("Star TSP143IV", printer.Name);
        Assert.Equal("192.168.1.50", printer.Host);
        Assert.Equal(9100, printer.Port);
    }

    [Fact]
    public void Compression_pointers_are_followed_rather_than_read_as_labels()
    {
        // Every name past the first in the packet built below is a pointer. A parser
        // that read the two pointer bytes as a label length and a character would give
        // the printer a two-character name and no address at all — and would report an
        // empty list rather than an error.
        var packet = StarResponse("Counter 2 printer", "10.0.0.7", 9100);

        var records = MdnsMessage.Parse(packet);

        Assert.Equal(
            "Counter 2 printer._pdl-datastream._tcp.local",
            Assert.Single(records.Pointers).Instance);
        Assert.Equal(
            "star-tsp143iv.local",
            Assert.Single(records.Services).Value.Host);
        Assert.Equal("10.0.0.7", Assert.Single(records.Addresses).Value.ToString());
    }

    [Fact]
    public void A_name_ends_after_its_pointer_and_not_after_what_it_points_at()
    {
        // The detail that decides whether the rest of the packet parses. The name at
        // this position is two bytes long however long the name it resolves to is; an
        // offset left at the end of the target would read the next record from the
        // wrong place and lose every record after the first.
        var packet = StarResponse("Star TSP143IV", "192.168.1.50", 9100);

        var records = MdnsMessage.Parse(packet);

        // Three records after the answer's own name were only reachable by leaving the
        // offset in the right place.
        Assert.Single(records.Pointers);
        Assert.Single(records.Services);
        Assert.Single(records.Addresses);
    }

    [Fact]
    public void A_pointer_that_leads_forward_or_loops_is_abandoned_rather_than_chased()
    {
        // A malformed frame from anything on the subnet. A parser that chased this
        // would never return, and a discovery that never returns is a screen that
        // never comes back.
        var packet = new byte[]
        {
            0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 0,
            0xC0, 0x0C,                                 // a name pointing at itself
            0, 12, 0, 1, 0, 0, 0, 60, 0, 2, 0xC0, 0x0C
        };

        var records = MdnsMessage.Parse(packet);

        Assert.Empty(MdnsMessage.Printers(records));
    }

    [Fact]
    public void A_truncated_packet_yields_what_was_readable_and_no_exception()
    {
        var full = StarResponse("Star TSP143IV", "192.168.1.50", 9100);

        for (var cut = 12; cut < full.Length; cut += 3)
        {
            var records = MdnsMessage.Parse(full[..cut]);

            // The assertion is that it returns. A device answering with half a frame
            // costs its own entry and nothing else.
            Assert.NotNull(records);
            Assert.NotNull(MdnsMessage.Printers(records));
        }
    }

    [Fact]
    public void Nothing_a_fragment_and_a_header_alone_all_decode_as_nothing()
    {
        Assert.Empty(MdnsMessage.Printers(MdnsMessage.Parse(Array.Empty<byte>())));
        Assert.Empty(MdnsMessage.Printers(MdnsMessage.Parse(new byte[] { 1, 2, 3 })));
        Assert.Empty(MdnsMessage.Printers(MdnsMessage.Parse(new byte[12])));
    }

    [Fact]
    public void A_pointer_with_no_service_record_is_dropped_rather_than_guessed_at()
    {
        // The instance name would give a plausible host. A plausible host is exactly
        // the kind of invention this product does not make: without an SRV there is no
        // port, and without a port there is no printer, only a name.
        var packet = PointerOnly("Star TSP143IV");

        var records = MdnsMessage.Parse(packet);

        Assert.Single(records.Pointers);
        Assert.Empty(MdnsMessage.Printers(records));
    }

    [Fact]
    public void An_srv_with_no_address_record_falls_back_to_the_host_name()
    {
        // Some responders answer without the A record and expect the caller to resolve
        // the name. A host name opens a socket perfectly well, so this is used rather
        // than discarded — and the trailing dot is stripped because a socket does not
        // want it.
        var packet = StarResponse("Star TSP143IV", address: null, port: 9100);

        var printer = Assert.Single(MdnsMessage.Printers(MdnsMessage.Parse(packet)));

        Assert.Equal("star-tsp143iv.local", printer.Host);
    }

    [Fact]
    public void The_instance_label_is_the_first_label_and_an_escaped_dot_stays_in_it()
    {
        Assert.Equal(
            "Star TSP143IV",
            MdnsMessage.InstanceLabel("Star TSP143IV._pdl-datastream._tcp.local"));

        // A label may contain a dot, escaped in the presentation form. Splitting on
        // dots would cut "Counter 2" out of "Counter 2.5".
        Assert.Equal(
            "Counter 2.5",
            MdnsMessage.InstanceLabel(@"Counter 2\.5._pdl-datastream._tcp.local"));

        // A name with no service suffix is its own label rather than nothing.
        Assert.Equal("bare", MdnsMessage.InstanceLabel("bare"));
    }

    // ─── Packets, built the way a responder builds them ──────────────────────

    /// <summary>
    /// A PTR, an SRV and optionally an A, with every name after the first written as a
    /// compression pointer — which is what Bonjour and avahi actually put on the wire.
    /// </summary>
    private static byte[] StarResponse(string instance, string? address, int port)
    {
        var packet = new PacketBuilder();

        packet.U16(0);                                  // id
        packet.U16(0x8400);                             // response, authoritative
        packet.U16(0);                                  // questions
        packet.U16(1);                                  // answers
        packet.U16(0);                                  // authority
        packet.U16(address is null ? 1 : 2);            // additional

        var serviceAt = packet.Name("_pdl-datastream", "_tcp", "local");
        packet.U16(12);                                 // PTR
        packet.U16(1);                                  // IN
        packet.U32(4500);

        var rdLength = packet.Mark();
        packet.U16(0);
        var instanceAt = packet.Mark();
        packet.Label(instance);
        packet.Pointer(serviceAt);
        packet.PatchLength(rdLength);

        packet.Pointer(instanceAt);
        packet.U16(33);                                 // SRV
        packet.U16(1);
        packet.U32(120);

        var srvLength = packet.Mark();
        packet.U16(0);
        packet.U16(0);                                  // priority
        packet.U16(0);                                  // weight
        packet.U16(port);
        var hostAt = packet.Name("star-tsp143iv", "local");
        packet.PatchLength(srvLength);

        if (address is not null)
        {
            packet.Pointer(hostAt);
            packet.U16(1);                              // A
            packet.U16(1);
            packet.U32(120);
            packet.U16(4);
            foreach (var octet in address.Split('.'))
            {
                packet.Byte(byte.Parse(octet));
            }
        }

        return packet.ToArray();
    }

    private static byte[] PointerOnly(string instance)
    {
        var packet = new PacketBuilder();

        packet.U16(0);
        packet.U16(0x8400);
        packet.U16(0);
        packet.U16(1);
        packet.U16(0);
        packet.U16(0);

        var serviceAt = packet.Name("_pdl-datastream", "_tcp", "local");
        packet.U16(12);
        packet.U16(1);
        packet.U32(4500);

        var rdLength = packet.Mark();
        packet.U16(0);
        packet.Label(instance);
        packet.Pointer(serviceAt);
        packet.PatchLength(rdLength);

        return packet.ToArray();
    }

    private sealed class PacketBuilder
    {
        private readonly List<byte> _bytes = new();

        public int Mark() => _bytes.Count;

        public void Byte(byte value) => _bytes.Add(value);

        public void U16(int value)
        {
            _bytes.Add((byte)(value >> 8));
            _bytes.Add((byte)(value & 0xFF));
        }

        public void U32(uint value)
        {
            _bytes.Add((byte)(value >> 24));
            _bytes.Add((byte)(value >> 16));
            _bytes.Add((byte)(value >> 8));
            _bytes.Add((byte)value);
        }

        public void Label(string label)
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            _bytes.Add((byte)bytes.Length);
            _bytes.AddRange(bytes);
        }

        /// <summary>A full name, and where it starts, so later records can point at it.</summary>
        public int Name(params string[] labels)
        {
            var at = _bytes.Count;
            foreach (var label in labels)
            {
                Label(label);
            }

            _bytes.Add(0);
            return at;
        }

        public void Pointer(int offset) => U16(0xC000 | offset);

        /// <summary>Write the RDLENGTH now that the RDATA is written.</summary>
        public void PatchLength(int at)
        {
            var length = _bytes.Count - at - 2;
            _bytes[at] = (byte)(length >> 8);
            _bytes[at + 1] = (byte)(length & 0xFF);
        }

        public byte[] ToArray() => _bytes.ToArray();
    }
}
