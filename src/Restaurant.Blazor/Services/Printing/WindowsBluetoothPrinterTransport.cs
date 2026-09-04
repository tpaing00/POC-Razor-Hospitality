using System.Net.Sockets;
using System.Runtime.InteropServices;
using Restaurant.UI.Shared.Services.Printing;

namespace Restaurant.Blazor.Services.Printing;

/// <summary>
/// <see cref="IPrinterTransport"/> over the <b>host's</b> Bluetooth radio, for a back
/// office served from a machine that has one.
///
/// **Read this paragraph before believing anything else on this screen.**
/// <c>Restaurant.Blazor</c> is Blazor Server: its C# runs in the ASP.NET process, not
/// in the browser. So "Bluetooth in the back office" means the radio in the machine
/// running the server — a counter PC, a mini-PC in the office, the tablet if somebody
/// serves the back office from the terminal itself. It is never the radio in the
/// laptop of a manager who opened the page from home. A manager on a different machine
/// who scans here is scanning the server's surroundings, and the screen says which
/// host it is speaking for so that is not a surprise.
///
/// **.NET has no cross-platform Bluetooth Classic API, and this is what is actually
/// available.** There is no <c>System.Bluetooth</c>. What exists on Windows is two
/// native surfaces, both used here and neither needing a package:
/// <list type="bullet">
/// <item><c>bthprops.cpl</c> — <c>BluetoothFindFirstRadio</c> answers whether the host
/// has a radio at all, and <c>BluetoothFindFirstDevice</c>/<c>FindNextDevice</c>
/// enumerates what it knows about.</item>
/// <item>Winsock's <c>AF_BTH</c> address family — a plain
/// <see cref="System.Net.Sockets.Socket"/> with address family 32 and protocol 3 is an
/// RFCOMM connection, addressed by a <c>SOCKADDR_BTH</c> carrying the device address
/// and the Serial Port Profile service GUID. The kernel does the SDP lookup and
/// returns a stream. Once it is open it is the same socket the network transport
/// hands back and the same bytes go down it.</item>
/// </list>
///
/// **What it can do:** say honestly whether the host has a radio; list devices the
/// host already knows (paired, or seen recently); optionally run a short inquiry for
/// devices it does not; open RFCOMM to a paired Star unit and print.
///
/// **What it cannot do, and no amount of code here would change:**
/// <list type="bullet">
/// <item><b>Pair.</b> Pairing raises a system consent dialog that belongs to the
/// Windows shell, on the machine with the radio. A server process cannot show it and a
/// browser cannot see it. An unpaired device is listed and labelled, and the copy sends
/// the person to Windows Settings on the host.</item>
/// <item><b>Run anywhere but Windows.</b> On Linux the equivalent is BlueZ over D-Bus
/// and on macOS it is IOBluetooth; neither is written here and
/// <see cref="IsAvailable"/> says so plainly rather than producing an empty list.</item>
/// <item><b>Be the Android terminal's transport.</b> That is
/// <c>Restaurant.Mobile</c>'s <c>BluetoothPrinterTransport</c>, which is verified
/// against real hardware and is untouched by this. When the back office is served from
/// the Android device itself, that is the implementation with the radio and this one
/// reports no radio — which is correct, because on that host this code is not the
/// thing holding it.</item>
/// </list>
/// </summary>
public sealed class WindowsBluetoothPrinterTransport : IPrinterTransport
{
    /// <summary>The Serial Port Profile service class. The same well-known UUID the
    /// Android transport opens an RFCOMM socket to, because it is the printer's side of
    /// the arrangement and does not vary by host.</summary>
    public static readonly Guid SerialPortProfile = new("00001101-0000-1000-8000-00805F9B34FB");

    /// <summary>Winsock's <c>AF_BTH</c>.</summary>
    private const int AddressFamilyBluetooth = 32;

    /// <summary>Winsock's <c>BTHPROTO_RFCOMM</c>.</summary>
    private const int ProtocolRfcomm = 3;

    /// <summary>
    /// How long an inquiry runs, in Windows' units of 1.28 seconds. Two is about two
    /// and a half seconds — the same order as the mDNS window, and short because an
    /// inquiry saturates the radio while it runs and a person is waiting on it.
    /// </summary>
    private const byte InquiryTimeoutMultiplier = 2;

    public string Name => "Bluetooth";

    public bool IsAvailable(out string? reason)
    {
        if (!OperatingSystem.IsWindows())
        {
            reason = "This host serves the back office from a system with no Bluetooth support built here · " +
                     "use a network printer, or run the back office on a Windows host";
            return false;
        }

        // Cached for a moment: the screen reads this several times per render, and
        // walking the radio list that often is real work for an answer that cannot have
        // changed between two frames of the same second. Short enough that plugging an
        // adapter in shows up on the next scan.
        lock (_radioLock)
        {
            if (DateTime.UtcNow - _radioCheckedAt < RadioCheckLife)
            {
                reason = _radioReason;
                return _radioReason is null;
            }

            try
            {
                // The distinction the whole feature turns on: a missing radio is not
                // "no printers found", it is "this machine cannot look".
                _radioReason = HasRadio()
                    ? null
                    : "This host has no Bluetooth radio · plug in an adapter on the machine serving the " +
                      "back office, or use a network printer";
            }
            catch (Exception ex)
            {
                _radioReason = $"Could not read this host's Bluetooth radio · {ex.Message}";
            }

            _radioCheckedAt = DateTime.UtcNow;
            reason = _radioReason;
            return _radioReason is null;
        }
    }

    private static readonly TimeSpan RadioCheckLife = TimeSpan.FromSeconds(5);
    private readonly object _radioLock = new();
    private DateTime _radioCheckedAt = DateTime.MinValue;
    private string? _radioReason;

    /// <summary>
    /// The devices the host's radio knows: remembered and authenticated first, then a
    /// short inquiry for anything nearby it does not.
    ///
    /// A device that is not paired is listed and labelled, because listing it is the
    /// only way to tell somebody the printer is there and needs pairing on the host.
    /// Connecting to it will fail until that happens, and the copy says so before they
    /// try rather than after.
    /// </summary>
    public Task<IReadOnlyList<PrinterDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<IReadOnlyList<PrinterDevice>>(Array.Empty<PrinterDevice>());
        }

        // Every call below blocks — an inquiry is seconds of radio time — so the whole
        // enumeration goes on the pool. In a Blazor Server host a blocked thread is a
        // circuit that stops answering, which is the same failure the terminal avoids
        // for the same reason.
        return Task.Run<IReadOnlyList<PrinterDevice>>(
            () =>
            {
                var found = new Dictionary<ulong, PrinterDevice>();

                foreach (var device in Enumerate(issueInquiry: false))
                {
                    found[device.Address] = ToDevice(device);
                }

                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    foreach (var device in Enumerate(issueInquiry: true))
                    {
                        // Known devices already have the better record — the inquiry
                        // result may not carry the authenticated flag — so it only adds.
                        if (!found.ContainsKey(device.Address))
                        {
                            found[device.Address] = ToDevice(device);
                        }
                    }
                }
                catch (Exception)
                {
                    // An inquiry that will not run is not a discovery failure. The known
                    // devices above are the path that matters and are already listed —
                    // the same ruling the Android transport makes.
                }

                return found.Values
                    .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            },
            cancellationToken);
    }

    public async Task<IPrinterConnection> ConnectAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Bluetooth printing from the back office host is implemented for Windows only.");
        }

        var address = ParseAddress(deviceId);

        var socket = new Socket(
            (AddressFamily)AddressFamilyBluetooth,
            SocketType.Stream,
            (ProtocolType)ProtocolRfcomm);

        try
        {
            // The kernel resolves the Serial Port Profile GUID to a channel number by
            // asking the device's SDP server, which is what makes this a service
            // connect rather than a guess at a channel. It blocks, so it is on the
            // pool.
            await Task.Run(
                    () => socket.Connect(new BluetoothEndPoint(address, SerialPortProfile)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            socket.Dispose();
            throw;
        }

        return new WindowsBluetoothConnection(socket);
    }

    /// <summary>
    /// A 48-bit Bluetooth address from the form the screen shows,
    /// <c>00:11:22:33:44:55</c>. Hyphens and no separator at all also parse, because
    /// Windows writes them three different ways in three different places.
    /// </summary>
    internal static ulong ParseAddress(string deviceId)
    {
        var text = new string((deviceId ?? string.Empty)
            .Where(char.IsAsciiHexDigit)
            .ToArray());

        if (text.Length != 12 || !ulong.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var value))
        {
            throw new FormatException(
                $"“{deviceId}” is not a Bluetooth address · it is twelve hex digits, like 00:11:22:33:44:55");
        }

        return value;
    }

    /// <summary>The address as a person reads it, and as this parses it back.</summary>
    internal static string FormatAddress(ulong address) => string.Join(
        ':',
        Enumerable.Range(0, 6).Select(i => ((byte)(address >> ((5 - i) * 8))).ToString("X2")));

    private static PrinterDevice ToDevice(BluetoothDeviceInfo device)
    {
        var address = FormatAddress(device.Address);
        var name = string.IsNullOrWhiteSpace(device.Name) ? address : device.Name.Trim();

        // Every device the radio knows is offered, not only the ones Windows classes as
        // a printer. A Star unit's device class is not reliably Imaging, and a filter
        // that hides the printer somebody is trying to reach is worse than a list with
        // a pair of headphones in it — the same call the Android transport makes.
        return new PrinterDevice(
            address,
            name,
            device.Authenticated,
            PairingNote: device.Authenticated
                ? null
                // Nothing prompts on a server. The pairing dialog belongs to the Windows
                // shell on the machine with the radio, and a person has to go to it.
                : "This printer is not paired with this host · pair it in Windows Settings on the machine " +
                  "serving the back office, then print a test label");
    }

    // ─── Native ──────────────────────────────────────────────────────────────

    private static bool HasRadio()
    {
        var parameters = new BluetoothFindRadioParams { Size = Marshal.SizeOf<BluetoothFindRadioParams>() };
        var find = BluetoothFindFirstRadio(ref parameters, out var radio);

        if (find == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return true;
        }
        finally
        {
            if (radio != IntPtr.Zero)
            {
                CloseHandle(radio);
            }

            BluetoothFindRadioClose(find);
        }
    }

    /// <summary>
    /// Walk the radio's device list once.
    ///
    /// <paramref name="issueInquiry"/> false returns what Windows already holds — the
    /// devices somebody paired in Settings, plus ones seen recently — and returns
    /// immediately. True runs an actual radio inquiry and takes seconds. They are two
    /// calls rather than one because the first is the path that works and must not be
    /// made to wait on the second.
    /// </summary>
    private static IEnumerable<BluetoothDeviceInfo> Enumerate(bool issueInquiry)
    {
        var search = new BluetoothDeviceSearchParams
        {
            Size = Marshal.SizeOf<BluetoothDeviceSearchParams>(),
            ReturnAuthenticated = true,
            ReturnRemembered = true,
            ReturnUnknown = issueInquiry,
            ReturnConnected = true,
            IssueInquiry = issueInquiry,
            TimeoutMultiplier = issueInquiry ? InquiryTimeoutMultiplier : (byte)0,
            Radio = IntPtr.Zero
        };

        var info = new BluetoothDeviceInfo { Size = Marshal.SizeOf<BluetoothDeviceInfo>() };
        var find = BluetoothFindFirstDevice(ref search, ref info);

        if (find == IntPtr.Zero)
        {
            yield break;
        }

        try
        {
            do
            {
                yield return info;
                info = new BluetoothDeviceInfo { Size = Marshal.SizeOf<BluetoothDeviceInfo>() };
            }
            while (BluetoothFindNextDevice(find, ref info));
        }
        finally
        {
            BluetoothFindDeviceClose(find);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothFindRadioParams
    {
        public int Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothDeviceSearchParams
    {
        public int Size;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnAuthenticated;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnUnknown;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool IssueInquiry;
        public byte TimeoutMultiplier;
        public IntPtr Radio;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BluetoothDeviceInfo
    {
        public int Size;
        public ulong Address;
        public uint ClassOfDevice;
        [MarshalAs(UnmanagedType.Bool)] public bool Connected;
        [MarshalAs(UnmanagedType.Bool)] public bool Remembered;
        [MarshalAs(UnmanagedType.Bool)] public bool Authenticated;
        public SystemTime LastSeen;
        public SystemTime LastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(
        ref BluetoothFindRadioParams parameters,
        out IntPtr radio);

    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindRadioClose(IntPtr find);

    [DllImport("bthprops.cpl", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr BluetoothFindFirstDevice(
        ref BluetoothDeviceSearchParams search,
        ref BluetoothDeviceInfo info);

    [DllImport("bthprops.cpl", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextDevice(IntPtr find, ref BluetoothDeviceInfo info);

    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindDeviceClose(IntPtr find);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>
    /// A <c>SOCKADDR_BTH</c>, which is the one thing .NET has no type for.
    ///
    /// The layout is fixed by Winsock: a two-byte address family, then the 48-bit
    /// device address widened to eight bytes at offset 8, then the sixteen-byte service
    /// class GUID at offset 16, then a four-byte port at offset 32 — forty bytes with
    /// the alignment padding. A port of zero with a service GUID set is what tells the
    /// kernel to look the channel up over SDP instead of being told it, which is the
    /// difference between connecting to the printer's serial service and connecting to
    /// whatever happens to be on channel one.
    /// </summary>
    private sealed class BluetoothEndPoint(ulong address, Guid service) : System.Net.EndPoint
    {
        private const int SockAddrLength = 40;
        private const int AddressOffset = 8;
        private const int ServiceOffset = 16;
        private const int PortOffset = 32;

        public override AddressFamily AddressFamily => (AddressFamily)AddressFamilyBluetooth;

        public override System.Net.SocketAddress Serialize()
        {
            var socketAddress = new System.Net.SocketAddress(AddressFamily, SockAddrLength);

            for (var i = 0; i < 8; i++)
            {
                socketAddress[AddressOffset + i] = (byte)(address >> (i * 8));
            }

            var guid = service.ToByteArray();
            for (var i = 0; i < guid.Length; i++)
            {
                socketAddress[ServiceOffset + i] = guid[i];
            }

            for (var i = 0; i < 4; i++)
            {
                socketAddress[PortOffset + i] = 0;
            }

            return socketAddress;
        }

        public override System.Net.EndPoint Create(System.Net.SocketAddress socketAddress) => this;

        public override string ToString() => FormatAddress(address);
    }

    /// <summary>
    /// One open RFCOMM socket on the host's radio. Written to and closed after every
    /// job, exactly like the other two transports: this is the same contract, and
    /// <see cref="TransportReceiptPrinter"/> must not be able to tell which one it has.
    /// </summary>
    private sealed class WindowsBluetoothConnection(Socket socket) : IPrinterConnection
    {
        public async Task WriteAsync(byte[] payload, CancellationToken cancellationToken = default)
        {
            var sent = 0;
            while (sent < payload.Length)
            {
                var count = await socket
                    .SendAsync(payload.AsMemory(sent), SocketFlags.None, cancellationToken)
                    .ConfigureAwait(false);

                if (count <= 0)
                {
                    throw new IOException("the printer closed the connection while the ticket was being sent");
                }

                sent += count;
            }
        }

        public async Task<byte[]> ReadAsync(TimeSpan wait, CancellationToken cancellationToken = default)
        {
            var buffer = new byte[64];

            using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            window.CancelAfter(wait);

            try
            {
                var count = await socket.ReceiveAsync(buffer, SocketFlags.None, window.Token)
                    .ConfigureAwait(false);
                return count <= 0 ? Array.Empty<byte>() : buffer[..count];
            }
            catch (Exception)
            {
                // Nothing came back inside the window. A normal outcome the caller
                // records as an unread status rather than as health.
                return Array.Empty<byte>();
            }
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                socket.Shutdown(SocketShutdown.Both);
            }
            catch (Exception)
            {
            }

            socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
