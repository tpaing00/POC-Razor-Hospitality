using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Restaurant.UI.Shared.Services.Printing.Network;

/// <summary>
/// The fallback when multicast does not cross: try port 9100 on the local subnet and
/// see what answers.
///
/// **This runs second and only when mDNS found nothing, and that is the whole of its
/// justification.** A probe is a hundreds-of-connections event on a wire that is also
/// carrying card terminals, and a discovery design that opens with one is a design
/// that treats a restaurant like a lab. mDNS costs one multicast frame and gets a name
/// with the address; the probe costs a connect attempt per host and gets an address
/// with no name. So the cheap, precise, well-named path goes first, and this exists
/// for the network where it is dropped — client isolation on a shared access point,
/// two VLANs, a switch with IGMP snooping and no querier. Those are all real and all
/// common, which is why the fallback exists at all.
///
/// **Every bound on it is deliberate.**
/// <list type="bullet">
/// <item>Only a prefix of /24 or narrower is swept. A /16 is 65,534 hosts and there is
/// no timeout short enough to make that acceptable; a host on one gets the manual
/// address field instead, and is told so.</item>
/// <item>At most <see cref="MaxHosts"/> addresses, so a /23 misconfigured as /24
/// cannot surprise anybody.</item>
/// <item><see cref="MaxConcurrent"/> connections in flight. Not one per host: a burst
/// of 254 simultaneous SYNs is what a switch reads as a scan.</item>
/// <item>A per-host connect timeout of <see cref="ConnectTimeout"/>, and a whole-sweep
/// budget the caller sets. A discovery a person is waiting on has to end.</item>
/// <item>Loopback, tunnel and down interfaces are skipped, so a development machine
/// with three virtual adapters sweeps the one subnet the printer is on.</item>
/// </list>
///
/// **A host that accepts on 9100 is not proof of a printer**, and the name this
/// produces says so: an address and nothing else. Only the test label proves it
/// prints, which is the same rule the rest of this feature runs on.
/// </summary>
[UnsupportedOSPlatform("browser")]
internal static class SubnetProbe
{
    /// <summary>Star's raw printing port, and the one this probes.</summary>
    public const int RawPrintingPort = 9100;

    /// <summary>The narrowest prefix that is swept. /24 is 254 hosts; anything wider is
    /// refused rather than truncated, because a truncated sweep of a /16 finds the
    /// first 254 addresses and none of them is the printer.</summary>
    public const int MinimumPrefixLength = 24;

    /// <summary>A ceiling independent of the prefix, so a bad netmask cannot turn into
    /// a long sweep.</summary>
    public const int MaxHosts = 254;

    /// <summary>Connections in flight at once.</summary>
    public const int MaxConcurrent = 24;

    /// <summary>How long one host gets to accept.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// The addresses on one interface worth trying: every host in the subnet except the
    /// network address, the broadcast address and this host itself.
    ///
    /// Pure, and the reason the bounds are testable without a network.
    /// </summary>
    public static IReadOnlyList<IPAddress> Candidates(IPAddress local, int prefixLength)
    {
        ArgumentNullException.ThrowIfNull(local);

        if (local.AddressFamily != AddressFamily.InterNetwork || prefixLength < MinimumPrefixLength || prefixLength > 30)
        {
            // Refused, not truncated. A /16 has no bounded sweep and a /31 has no hosts.
            return Array.Empty<IPAddress>();
        }

        var host = ToUInt32(local);
        var mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
        var network = host & mask;
        var broadcast = network | ~mask;

        var addresses = new List<IPAddress>();
        for (var candidate = network + 1; candidate < broadcast && addresses.Count < MaxHosts; candidate++)
        {
            if (candidate == host)
            {
                continue;
            }

            addresses.Add(ToAddress(candidate));
        }

        return addresses;
    }

    /// <summary>
    /// The IPv4 subnets this host is actually on: up, not loopback, not a tunnel, and
    /// narrow enough to sweep.
    /// </summary>
    public static IReadOnlyList<(IPAddress Local, int PrefixLength)> LocalSubnets()
    {
        var subnets = new List<(IPAddress, int)>();

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                foreach (var address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork ||
                        IPAddress.IsLoopback(address.Address))
                    {
                        continue;
                    }

                    var prefix = address.PrefixLength;
                    if (prefix is > 0 and <= 32)
                    {
                        subnets.Add((address.Address, prefix));
                    }
                }
            }
        }
        catch (Exception)
        {
            // A host that will not describe its own interfaces gets no sweep. The
            // manual address field is the answer, and it is always on screen.
        }

        return subnets;
    }

    /// <summary>
    /// Try to open <see cref="RawPrintingPort"/> on each address, at most
    /// <see cref="MaxConcurrent"/> at a time, and return the ones that accepted.
    ///
    /// The socket is closed the moment it opens. Nothing is written: a byte sent to
    /// something that is not a printer is a byte some other device has to decide what to
    /// do with, and a connect that is accepted and closed is the smallest question this
    /// can ask.
    /// </summary>
    public static async Task<IReadOnlyList<IPAddress>> ScanAsync(
        IReadOnlyList<IPAddress> addresses,
        CancellationToken cancellationToken = default)
    {
        var answered = new List<IPAddress>();
        using var gate = new SemaphoreSlim(MaxConcurrent, MaxConcurrent);

        var attempts = addresses.Select(async address =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (await AcceptsAsync(address, cancellationToken).ConfigureAwait(false))
                {
                    lock (answered)
                    {
                        answered.Add(address);
                    }
                }
            }
            finally
            {
                gate.Release();
            }
        });

        try
        {
            await Task.WhenAll(attempts).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Cancellation, or a socket the host refused to create. Whatever answered
            // before that is still a useful list.
        }

        return answered.OrderBy(ToUInt32).ToList();
    }

    private static async Task<bool> AcceptsAsync(IPAddress address, CancellationToken cancellationToken)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectTimeout);

        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, RawPrintingPort), timeout.Token)
                .ConfigureAwait(false);
            return socket.Connected;
        }
        catch (Exception)
        {
            // Refused, unreachable, or out of time. All three mean nothing is listening
            // there as far as this is concerned.
            return false;
        }
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress ToAddress(uint value) => new(new[]
    {
        (byte)(value >> 24),
        (byte)(value >> 16),
        (byte)(value >> 8),
        (byte)value
    });
}
