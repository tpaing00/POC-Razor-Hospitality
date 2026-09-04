using System.Net;
using System.Text;

namespace Restaurant.UI.Shared.Services.Printing.Network;

/// <summary>
/// The multicast DNS packet, built and read. A pure function in both directions, and
/// the printing equivalent of <see cref="BagTicket"/>: it takes bytes and gives
/// records, opens no socket and holds no state, so it is the part of network discovery
/// that can be tested with no network in the room.
///
/// **Why the wire format is written out here rather than taken from a package.** The
/// query this build sends is one packet with one question in it, and the answer it
/// reads is four record types out of the sixty DNS defines. A dependency for that
/// would bring a resolver, a cache, a responder and a service registry into a POC that
/// needs none of them, and would put the one piece of this feature that is genuinely
/// fiddly — name compression — behind somebody else's version number. Written out, it
/// is two hundred lines with tests on the byte level.
///
/// **Name compression is the fiddly part and the reason these tests exist.** A DNS
/// name is a sequence of length-prefixed labels ending in a zero byte, except that a
/// label whose top two bits are set is not a label at all: it is a fourteen-bit offset
/// into the packet where the rest of the name is written. Responders use it heavily —
/// a typical Bonjour answer writes <c>_pdl-datastream._tcp.local</c> once and points at
/// it four times — so a parser that does not follow pointers reads a printer's name as
/// two characters of rubbish. A parser that follows them without a bound loops forever
/// on a malformed packet from anything on the subnet, which is why the hop count is
/// capped.
/// </summary>
internal static class MdnsMessage
{
    /// <summary>The multicast address every mDNS responder listens on.</summary>
    public static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.251");

    /// <summary>The port, fixed by the specification.</summary>
    public const int MulticastPort = 5353;

    private const ushort TypeA = 1;
    private const ushort TypePtr = 12;
    private const ushort TypeTxt = 16;
    private const ushort TypeSrv = 33;

    /// <summary>Class IN, with the top bit spare for the unicast-response request.</summary>
    private const ushort ClassInternet = 1;
    private const ushort UnicastResponseBit = 0x8000;

    /// <summary>
    /// How many compression pointers one name may follow before the packet is called
    /// malformed. A name is at most 255 bytes and each hop must point backwards, so
    /// anything past a handful is a loop — and a loop here is a discovery that never
    /// returns on a subnet with one broken device on it.
    /// </summary>
    private const int MaxPointerHops = 16;

    /// <summary>
    /// One query asking for every service at once.
    ///
    /// **One packet, not one per service.** DNS allows several questions in a message
    /// and mDNS responders answer all of them, so asking for the three service types a
    /// Star unit might advertise costs the subnet one multicast frame rather than
    /// three. That matters here: §"discovery must not hammer the subnet" is the
    /// constraint this whole design is shaped by, and a restaurant network carries
    /// tills and card terminals on the same wire.
    /// </summary>
    /// <param name="serviceNames">Service types, dotted and without a trailing dot —
    /// <c>_pdl-datastream._tcp.local</c>.</param>
    /// <param name="unicastResponse">Ask responders to answer this socket directly
    /// rather than to the multicast group. Set when the socket could not take port
    /// 5353 because something else on the host already has it.</param>
    public static byte[] BuildQuery(IReadOnlyList<string> serviceNames, bool unicastResponse = false)
    {
        ArgumentNullException.ThrowIfNull(serviceNames);

        var packet = new List<byte>(96);

        // Transaction id 0. mDNS matches answers by name rather than by id, and a
        // responder that echoes an id at all echoes this one.
        Write16(packet, 0);

        // Flags: a standard query, no recursion. Every bit zero.
        Write16(packet, 0);

        Write16(packet, (ushort)serviceNames.Count);
        Write16(packet, 0);
        Write16(packet, 0);
        Write16(packet, 0);

        foreach (var service in serviceNames)
        {
            WriteName(packet, service);
            Write16(packet, TypePtr);
            Write16(packet, (ushort)(unicastResponse ? ClassInternet | UnicastResponseBit : ClassInternet));
        }

        return packet.ToArray();
    }

    /// <summary>
    /// Read whatever a responder sent. Never throws: a truncated or malformed packet
    /// yields whatever was readable before it stopped making sense, because the sender
    /// is an unauthenticated device on a shared network and a discovery that throws on
    /// a bad frame is a discovery one broken device can switch off.
    /// </summary>
    public static MdnsRecords Parse(byte[] packet)
    {
        var pointers = new List<MdnsPointer>();
        var services = new Dictionary<string, MdnsService>(StringComparer.OrdinalIgnoreCase);
        var addresses = new Dictionary<string, IPAddress>(StringComparer.OrdinalIgnoreCase);

        if (packet is null || packet.Length < 12)
        {
            return new MdnsRecords(pointers, services, addresses);
        }

        try
        {
            var offset = 0;
            offset += 2;                                    // transaction id
            offset += 2;                                    // flags
            var questions = Read16(packet, ref offset);
            var answers = Read16(packet, ref offset);
            var authorities = Read16(packet, ref offset);
            var additional = Read16(packet, ref offset);

            for (var i = 0; i < questions && offset < packet.Length; i++)
            {
                ReadName(packet, ref offset);
                offset += 4;                                // qtype, qclass
            }

            var records = answers + authorities + additional;
            for (var i = 0; i < records && offset < packet.Length; i++)
            {
                var name = ReadName(packet, ref offset);
                if (offset + 10 > packet.Length)
                {
                    break;
                }

                var type = Read16(packet, ref offset);
                offset += 2;                                // class, and the cache-flush bit
                offset += 4;                                // ttl
                var length = Read16(packet, ref offset);

                if (offset + length > packet.Length)
                {
                    break;
                }

                var end = offset + length;

                switch (type)
                {
                    case TypePtr:
                    {
                        var target = offset;
                        var instance = ReadName(packet, ref target);
                        if (instance.Length > 0)
                        {
                            pointers.Add(new MdnsPointer(name, instance));
                        }

                        break;
                    }

                    case TypeSrv when length >= 7:
                    {
                        var cursor = offset;
                        cursor += 2;                        // priority
                        cursor += 2;                        // weight
                        var port = Read16(packet, ref cursor);
                        var host = ReadName(packet, ref cursor);
                        if (host.Length > 0)
                        {
                            services[name] = new MdnsService(host, port);
                        }

                        break;
                    }

                    case TypeA when length == 4:
                    {
                        addresses[name] = new IPAddress(packet.AsSpan(offset, 4).ToArray());
                        break;
                    }

                    case TypeTxt:
                        // Read and discarded. A Star unit's TXT carries the model and
                        // the queue name, and neither is needed to open port 9100 —
                        // the instance name is what a person reads and the SRV is what
                        // the socket needs. Named here so the next reader knows it was
                        // considered rather than missed.
                        break;
                }

                offset = end;
            }
        }
        catch (Exception)
        {
            // Whatever was read before the packet stopped parsing is kept. A device
            // that answers with rubbish costs its own entry and nothing else.
        }

        return new MdnsRecords(pointers, services, addresses);
    }

    /// <summary>
    /// Turn records into printers: an instance name a person can read, and a host and
    /// port a socket can open.
    ///
    /// **A pointer with no service record is dropped rather than guessed at.** The
    /// instance name would give a plausible host — strip the service suffix, append
    /// <c>.local</c> — and a plausible host is exactly the kind of invention this
    /// product does not make. Without an SRV there is no port, and without a port there
    /// is no printer, only a name.
    /// </summary>
    public static IReadOnlyList<MdnsPrinter> Printers(MdnsRecords records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var found = new Dictionary<string, MdnsPrinter>(StringComparer.OrdinalIgnoreCase);

        // Every instance named by a pointer, plus every instance that arrived as a bare
        // SRV. Some responders answer a service query with the SRV and A alone, and a
        // parser that insists on the pointer misses them.
        var instances = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pointer in records.Pointers)
        {
            instances.Add(pointer.Instance);
        }

        foreach (var name in records.Services.Keys)
        {
            instances.Add(name);
        }

        foreach (var instance in instances)
        {
            if (!records.Services.TryGetValue(instance, out var service))
            {
                continue;
            }

            var host = records.Addresses.TryGetValue(service.Host, out var address)
                ? address.ToString()
                : service.Host.TrimEnd('.');

            if (host.Length == 0)
            {
                continue;
            }

            found[instance] = new MdnsPrinter(InstanceLabel(instance), host, service.Port);
        }

        return found.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// The first label of an instance name, which is the part somebody named the
    /// printer. <c>Star TSP143IV._pdl-datastream._tcp.local</c> is
    /// <c>Star TSP143IV</c>; the rest is the service type and means nothing to a person
    /// choosing between two printers.
    ///
    /// A label may contain a dot, escaped as <c>\.</c> in the presentation form, so the
    /// split walks the string rather than calling <c>Split</c>.
    /// </summary>
    internal static string InstanceLabel(string instance)
    {
        var label = new StringBuilder();
        for (var i = 0; i < instance.Length; i++)
        {
            var c = instance[i];
            if (c == '\\' && i + 1 < instance.Length)
            {
                label.Append(instance[++i]);
                continue;
            }

            if (c == '.')
            {
                break;
            }

            label.Append(c);
        }

        var text = label.ToString().Trim();
        return text.Length > 0 ? text : instance;
    }

    private static void Write16(List<byte> packet, ushort value)
    {
        packet.Add((byte)(value >> 8));
        packet.Add((byte)(value & 0xFF));
    }

    /// <summary>
    /// A dotted name as length-prefixed labels ending in a zero byte. A label longer
    /// than 63 bytes cannot be expressed and is truncated rather than corrupting the
    /// packet's framing — the length byte's top two bits are the compression marker,
    /// so a length of 64 would be read as a pointer.
    /// </summary>
    private static void WriteName(List<byte> packet, string name)
    {
        foreach (var label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            var length = Math.Min(bytes.Length, 63);
            packet.Add((byte)length);
            packet.AddRange(bytes.AsSpan(0, length).ToArray());
        }

        packet.Add(0);
    }

    private static ushort Read16(byte[] packet, ref int offset)
    {
        if (offset + 2 > packet.Length)
        {
            offset = packet.Length;
            return 0;
        }

        var value = (ushort)((packet[offset] << 8) | packet[offset + 1]);
        offset += 2;
        return value;
    }

    /// <summary>
    /// A name, following compression pointers. <paramref name="offset"/> is left after
    /// the name as it appears at this position — after the pointer, not after whatever
    /// the pointer led to, which is the detail that makes the difference between
    /// reading a packet and reading it twice.
    /// </summary>
    internal static string ReadName(byte[] packet, ref int offset)
    {
        var labels = new List<string>();
        var hops = 0;
        var cursor = offset;
        var jumped = false;

        while (cursor < packet.Length)
        {
            var length = packet[cursor];

            if (length == 0)
            {
                cursor++;
                break;
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (cursor + 1 >= packet.Length || ++hops > MaxPointerHops)
                {
                    cursor = packet.Length;
                    break;
                }

                var target = ((length & 0x3F) << 8) | packet[cursor + 1];

                if (!jumped)
                {
                    // The name at this position ends after the two pointer bytes,
                    // whatever it points at.
                    offset = cursor + 2;
                    jumped = true;
                }

                if (target >= packet.Length || target >= cursor)
                {
                    // A pointer must lead backwards. One that does not is either
                    // malformed or a loop, and neither is worth chasing.
                    break;
                }

                cursor = target;
                continue;
            }

            if (cursor + 1 + length > packet.Length)
            {
                break;
            }

            labels.Add(Encoding.UTF8.GetString(packet, cursor + 1, length));
            cursor += 1 + length;
        }

        if (!jumped)
        {
            offset = cursor;
        }

        return string.Join('.', labels);
    }
}

/// <summary>What one packet said, sorted by record type and nothing more.</summary>
internal sealed record MdnsRecords(
    IReadOnlyList<MdnsPointer> Pointers,
    IReadOnlyDictionary<string, MdnsService> Services,
    IReadOnlyDictionary<string, IPAddress> Addresses);

/// <summary>A PTR: this service type has an instance by this name.</summary>
internal sealed record MdnsPointer(string Service, string Instance);

/// <summary>An SRV: this instance is on this host at this port.</summary>
internal sealed record MdnsService(string Host, int Port);

/// <summary>One advertised printer, resolved to something a socket can open.</summary>
internal sealed record MdnsPrinter(string Name, string Host, int Port);
