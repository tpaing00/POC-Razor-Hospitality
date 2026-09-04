namespace Restaurant.UI.Shared.Services.Printing;

/// <summary>
/// Several transports behaving as one. The whole of multi-transport discovery, and
/// the reason nothing above <see cref="IPrinterTransport"/> had to move to get it.
///
/// **This is a transport, not a new layer.** <see cref="TransportReceiptPrinter"/>
/// holds the print state machine over one <see cref="IPrinterTransport"/>; making it
/// hold a list instead would have put fan-out, per-member failure handling and
/// connection routing inside the state machine, where they would sit next to the
/// seven states and the Star Line exchange and have nothing to do with either. A
/// composite that implements the same interface keeps the state machine reading as it
/// did, keeps its fifteen tests untouched, and makes aggregation a thing with its own
/// tests. The terminal registers one transport and never constructs this class; the
/// back office registers two and wraps them.
///
/// **Routing is a prefix on the id, and that is deliberate.**
/// <see cref="IPrinterTransport.ConnectAsync"/> takes an id and nothing else, and
/// <see cref="IPrinterPreference"/> remembers an id and nothing else, so the id is the
/// only thing that survives a restart. A device found by the network transport comes
/// back as <c>network/192.168.1.50:9100</c>: the prefix routes the connection and the
/// remembered pairing still routes correctly next morning. <c>/</c> is the separator
/// because it appears in neither a MAC address nor a host and port, and
/// <see cref="PrinterDevice.Address"/> carries the bare address so no screen ever has
/// to show the routing key.
/// </summary>
public sealed class CompositePrinterTransport : IAddressablePrinterTransport
{
    /// <summary>
    /// Separates the transport key from the transport's own device id. Not a colon,
    /// because a MAC address is six of those and a host and port is one more.
    /// </summary>
    public const char Separator = '/';

    private readonly IReadOnlyList<(string Key, IPrinterTransport Transport)> _members;

    public CompositePrinterTransport(IEnumerable<IPrinterTransport> transports)
    {
        ArgumentNullException.ThrowIfNull(transports);

        var members = new List<(string, IPrinterTransport)>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var transport in transports)
        {
            // The key has to be stable across restarts, because it is half of the id
            // the preference remembers. It comes from the transport's own name, and
            // two transports sharing a name are separated by their registration
            // order — which is fixed by the host's DI registration and therefore just
            // as stable.
            var key = Keyed(transport.Name);
            if (!used.Add(key))
            {
                var n = 2;
                while (!used.Add($"{key}{n}"))
                {
                    n++;
                }

                key = $"{key}{n}";
            }

            members.Add((key, transport));
        }

        _members = members;
    }

    /// <summary>The members, in registration order. The host decides which transport a
    /// person sees first by the order it registers them.</summary>
    public IReadOnlyList<IPrinterTransport> Members => _members.Select(m => m.Transport).ToList();

    public string Name => "All transports";

    /// <summary>
    /// Available when any member is.
    ///
    /// **A member that cannot run is not a failure of the whole.** A back office on a
    /// desktop with no Bluetooth radio still prints over the network, and reporting the
    /// composite unavailable because one member is would take the working half off the
    /// screen. When nothing at all can run, the reason is every member's reason joined,
    /// because at that point each one is a separate thing a person could fix.
    /// </summary>
    public bool IsAvailable(out string? reason)
    {
        if (_members.Count == 0)
        {
            reason = "This host has no printer transport registered, so it cannot reach a printer.";
            return false;
        }

        var reasons = new List<string>();
        foreach (var (_, transport) in _members)
        {
            if (transport.IsAvailable(out var why))
            {
                reason = null;
                return true;
            }

            if (why is { Length: > 0 })
            {
                reasons.Add($"{transport.Name} · {why}");
            }
        }

        reason = reasons.Count > 0
            ? string.Join(" — ", reasons)
            : "No transport on this host can reach a printer.";
        return false;
    }

    /// <summary>One row per member, flattened, so a nested composite still reports its
    /// leaves rather than itself.</summary>
    public IReadOnlyList<TransportAvailability> Describe() =>
        _members.SelectMany(m => m.Transport.Describe()).ToList();

    /// <summary>
    /// Ask every available member at once, and let each fail on its own.
    ///
    /// **The members run in parallel and their failures are isolated**, because they
    /// are bounded by different things: an mDNS window is two and a half seconds of
    /// waiting, a Bluetooth inquiry is twelve, and running them in sequence would make
    /// a person wait for the sum of two clocks that could have run together. A member
    /// that throws contributes nothing and is reported through
    /// <see cref="Describe"/>-style availability rather than taking the other member's
    /// results down with it — a Bluetooth stack that raises on enumeration must not
    /// hide two printers the network found.
    ///
    /// A member that says it is unavailable is not dialled at all. Asking a transport
    /// that has already said it has no radio is how a screen ends up rendering a stack
    /// trace instead of a sentence.
    /// </summary>
    public async Task<IReadOnlyList<PrinterDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var runs = _members
            .Where(m => m.Transport.IsAvailable(out _))
            .Select(m => (m.Key, m.Transport, Task: SafeDiscoverAsync(m.Transport, cancellationToken)))
            .ToList();

        await Task.WhenAll(runs.Select(r => r.Task)).ConfigureAwait(false);

        var results = new List<PrinterDevice>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Registration order outside, name order inside. A person scanning the list is
        // reading transports as groups, and a list that reshuffles between scans
        // because two devices answered in a different order is a list nobody can point
        // at.
        foreach (var (key, transport, task) in runs)
        {
            foreach (var device in task.Result.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var routed = Route(key, transport.Name, device);
                if (seen.Add(routed.Id))
                {
                    results.Add(routed);
                }
            }
        }

        return results;
    }

    private static async Task<IReadOnlyList<PrinterDevice>> SafeDiscoverAsync(
        IPrinterTransport transport,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transport.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // One transport failing is one transport's list being empty. Its
            // availability row is where a person is told why; taking the whole
            // discovery down here would spend a red error on a working half.
            return Array.Empty<PrinterDevice>();
        }
    }

    public Task<IPrinterConnection> ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var (transport, inner) = Resolve(deviceId);
        return transport.ConnectAsync(inner, cancellationToken);
    }

    /// <summary>
    /// The first member that can take a typed address takes it.
    ///
    /// The back office registers the network transport first for exactly this reason:
    /// an address somebody typed off a self-test page is an IP address, and there is
    /// no second candidate. If a host ever registers two addressable transports, the
    /// order it registers them in is the order this picks.
    /// </summary>
    public async Task<PrinterDevice> ResolveAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        foreach (var (key, transport) in _members)
        {
            if (transport is IAddressablePrinterTransport addressable && transport.IsAvailable(out _))
            {
                var device = await addressable.ResolveAsync(address, cancellationToken).ConfigureAwait(false);
                return Route(key, transport.Name, device);
            }
        }

        throw new InvalidOperationException(
            "No transport on this host takes a typed address · use scan instead");
    }

    /// <summary>Whether any available member takes a typed address. A composite is
    /// available when any member is, which is not the same as any member taking an
    /// address, so this is overridden rather than inherited.</summary>
    public bool AcceptsAddress =>
        _members.Any(m => m.Transport is IAddressablePrinterTransport && m.Transport.IsAvailable(out _));

    /// <summary>
    /// Put the routing key on the front of the id, the transport's name on the device,
    /// and the transport's own id where a screen can read it.
    /// </summary>
    private static PrinterDevice Route(string key, string transportName, PrinterDevice device) =>
        device with
        {
            Id = $"{key}{Separator}{device.Id}",
            Transport = transportName,
            Address = device.Address is { Length: > 0 } ? device.Address : device.Id
        };

    /// <summary>
    /// Split a routed id back into the member that owns it and the id that member
    /// understands. An id with no prefix, or a prefix naming nothing registered here,
    /// is a remembered pairing from a host that had a different set of transports —
    /// which is a thing that happens and has to say so rather than throw a null.
    /// </summary>
    private (IPrinterTransport Transport, string DeviceId) Resolve(string deviceId)
    {
        var cut = deviceId.IndexOf(Separator);
        if (cut > 0)
        {
            var key = deviceId[..cut];
            foreach (var (memberKey, transport) in _members)
            {
                if (string.Equals(memberKey, key, StringComparison.Ordinal))
                {
                    return (transport, deviceId[(cut + 1)..]);
                }
            }
        }

        throw new InvalidOperationException(
            $"No transport on this host handles {deviceId} · scan again and pick the printer from the list");
    }

    /// <summary>Lower-case letters and digits only, so the key is stable, short and
    /// never contains the separator.</summary>
    private static string Keyed(string name)
    {
        var key = new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return key.Length > 0 ? key : "transport";
    }
}
