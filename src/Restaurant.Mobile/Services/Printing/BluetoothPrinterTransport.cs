#nullable enable

#if ANDROID
using Android.Bluetooth;
using Android.Content;
using Android.OS;
#endif

using Restaurant.UI.Shared.Services.Printing;

namespace Restaurant.Mobile.Services.Printing;

/// <summary>
/// <see cref="IPrinterTransport"/> over Bluetooth Classic RFCOMM — the host half of
/// handbook §12's "Printing from a shared component".
///
/// <c>Restaurant.UI.Shared</c> has no MAUI reference and no Android binding, so it
/// cannot open a socket any more than it could read a battery. It owns the interface;
/// this class is the one place in the product that knows what a MAC address is.
///
/// **The protocol, briefly.** A Star printer speaks Serial Port Profile. Find the
/// device on <c>BluetoothAdapter</c>, open an RFCOMM socket to the well-known SPP
/// service UUID <c>00001101-0000-1000-8000-00805F9B34FB</c>, and write Star Line
/// bytes to the socket's output stream. There is no framing and no handshake: what
/// goes down the stream is what the print head receives.
///
/// **Nothing here runs on the UI thread.** Every one of the Android calls below —
/// connect, write, read, and discovery's inquiry — blocks, and a blocked UI thread on
/// a terminal is a screen that stops answering taps mid-service. They are all behind
/// <c>Task.Run</c>, and every failure comes back as an exception the caller turns into
/// a sentence on the screen rather than a line in a log nobody reads.
/// </summary>
public sealed class BluetoothPrinterTransport : IPrinterTransport
{
    /// <summary>
    /// The Serial Port Profile service UUID. Every SPP device answers on it; it is a
    /// well-known constant rather than something discovered per printer.
    /// </summary>
    public const string SerialPortProfileUuid = "00001101-0000-1000-8000-00805F9B34FB";

    /// <summary>
    /// How long an inquiry runs before it is cancelled. Android's own discovery is
    /// about twelve seconds and it saturates the radio while it runs, which is why it
    /// has to be stopped before a connect rather than left going.
    /// </summary>
    private static readonly TimeSpan InquiryWindow = TimeSpan.FromSeconds(12);

    /// <summary>
    /// Set once a permission request comes back refused, so the screen can say why
    /// rather than offering a scan that will silently find nothing. It is cleared on
    /// the next successful grant.
    /// </summary>
    private string? _permissionBlock;

    public string Name => "Bluetooth";

#if ANDROID
    public bool IsAvailable(out string? reason)
    {
        var adapter = Adapter;

        if (adapter is null)
        {
            reason = "This tablet has no Bluetooth radio, so it cannot reach a printer over Bluetooth.";
            return false;
        }

        if (!adapter.IsEnabled)
        {
            reason = "Bluetooth is off · switch it on in Android settings, then scan";
            return false;
        }

        if (_permissionBlock is not null)
        {
            reason = _permissionBlock;
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// The bonded set first, then whatever an inquiry adds.
    ///
    /// **Bonded devices are the path that works**, and they come first for that reason:
    /// a printer paired in Android settings connects with no system dialog, and on
    /// API 31 and up listing them needs only <c>BLUETOOTH_CONNECT</c>. The inquiry is
    /// for the printer that has never been paired with this tablet — it finds the unit,
    /// and connecting to it is what raises Android's own pairing prompt.
    /// </summary>
    public async Task<IReadOnlyList<PrinterDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync().ConfigureAwait(false);

        var adapter = Adapter ?? throw new InvalidOperationException("No Bluetooth adapter on this device.");
        var found = new Dictionary<string, PrinterDevice>(StringComparer.OrdinalIgnoreCase);

        foreach (var device in adapter.BondedDevices ?? new List<BluetoothDevice>())
        {
            if (device.Address is { Length: > 0 } address)
            {
                found[address] = new PrinterDevice(address, device.Name ?? address, IsPaired: true);
            }
        }

        // Every bonded device is offered, not only the ones Android classes as a
        // printer. A Star unit's device class is not reliably Imaging, and a filter
        // that hides the printer somebody is trying to pair is worse than a list with
        // a phone in it.
        if (_permissionBlock is null)
        {
            try
            {
                foreach (var device in await InquireAsync(adapter, cancellationToken).ConfigureAwait(false))
                {
                    found.TryAdd(device.Id, device);
                }
            }
            catch (Exception)
            {
                // An inquiry that will not start is not a discovery failure: the bonded
                // list above is the path that matters, and it is already populated.
                // Reporting a red error here would hide a working list behind a
                // permission the happy path does not need.
            }
        }

        return found.Values.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// An Android inquiry, which is a broadcast rather than a call: <c>StartDiscovery</c>
    /// returns immediately and every device found arrives as an <c>ACTION_FOUND</c>
    /// intent. This wraps that into one awaitable window and always cancels the
    /// inquiry, because an inquiry left running makes the next connect fail.
    /// </summary>
    private static async Task<IReadOnlyList<PrinterDevice>> InquireAsync(
        BluetoothAdapter adapter,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, PrinterDevice>(StringComparer.OrdinalIgnoreCase);
        var context = Android.App.Application.Context;

        using var receiver = new DeviceFoundReceiver(device =>
        {
            if (device.Address is { Length: > 0 } address)
            {
                lock (results)
                {
                    results[address] = new PrinterDevice(address, device.Name ?? address, IsPaired: false);
                }
            }
        });

        var filter = new IntentFilter(BluetoothDevice.ActionFound);
        context.RegisterReceiver(receiver, filter);

        try
        {
            if (!adapter.StartDiscovery())
            {
                return Array.Empty<PrinterDevice>();
            }

            await Task.Delay(InquiryWindow, cancellationToken).ConfigureAwait(false);
        }
        catch (System.OperationCanceledException)
        {
            // Qualified: Android.OS also declares an OperationCanceledException, and an
            // unqualified catch here binds to whichever the using directives reach
            // first. The one a cancelled Task.Delay throws is System's.
        }
        finally
        {
            try
            {
                adapter.CancelDiscovery();
            }
            catch (Exception)
            {
            }

            try
            {
                context.UnregisterReceiver(receiver);
            }
            catch (Exception)
            {
            }
        }

        lock (results)
        {
            return results.Values.ToList();
        }
    }

    public async Task<IPrinterConnection> ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync().ConfigureAwait(false);

        if (_permissionBlock is not null)
        {
            throw new InvalidOperationException(_permissionBlock);
        }

        var adapter = Adapter ?? throw new InvalidOperationException("No Bluetooth adapter on this device.");

        if (!adapter.IsEnabled)
        {
            throw new InvalidOperationException("Bluetooth is off");
        }

        var device = adapter.GetRemoteDevice(deviceId)
            ?? throw new InvalidOperationException($"No device at {deviceId}");

        // An inquiry in progress saturates the radio and makes a connect fail with a
        // read timeout that looks like a printer being off. Cancelling first is
        // documented Android practice and is the difference between "unreachable" and
        // "unreachable, sometimes, if you scanned recently".
        try
        {
            adapter.CancelDiscovery();
        }
        catch (Exception)
        {
        }

        var uuid = Java.Util.UUID.FromString(SerialPortProfileUuid)
            ?? throw new InvalidOperationException("Could not build the SPP service UUID");

        var socket = device.CreateRfcommSocketToServiceRecord(uuid)
            ?? throw new InvalidOperationException("Could not open an RFCOMM socket to this printer");

        try
        {
            // BluetoothSocket.Connect blocks until the far end answers or the stack
            // gives up, so it goes on the pool. This is the call that takes seconds
            // when the printer is off, and the one that must never be on the UI thread.
            await Task.Run(() => socket.Connect(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            socket.Dispose();
            throw;
        }

        return new BluetoothPrinterConnection(socket);
    }

    private static BluetoothAdapter? Adapter
    {
        get
        {
            try
            {
                var context = Android.App.Application.Context;
                if (context.GetSystemService(Context.BluetoothService) is BluetoothManager manager)
                {
                    return manager.Adapter;
                }
            }
            catch (Exception)
            {
            }

            return null;
        }
    }

    /// <summary>
    /// Ask for what this API level actually requires, and remember a refusal.
    ///
    /// **The permissions differ by API level and getting this wrong fails silently.**
    /// On API 31 and up <c>BLUETOOTH_CONNECT</c> and <c>BLUETOOTH_SCAN</c> are runtime
    /// permissions and the old <c>BLUETOOTH</c> pair is gone. Below 31 the old pair is
    /// install-time and needs no request, but an inquiry additionally needs a location
    /// permission — coarse from API 23, fine from API 29 — because Android treats
    /// discovering nearby radios as locating the user. The manifest declares both sets,
    /// each bounded by <c>maxSdkVersion</c> where it applies, and this method requests
    /// the runtime half of whichever set is live.
    ///
    /// The tablet this fleet has verified is API 28; the target tablet is unknown. Both
    /// branches ship.
    /// </summary>
    private async Task EnsurePermissionAsync()
    {
        try
        {
            var status = await MainThread
                .InvokeOnMainThreadAsync(async () =>
                {
                    var current = await Permissions.CheckStatusAsync<PrinterBluetoothPermission>()
                        .ConfigureAwait(false);

                    return current == PermissionStatus.Granted
                        ? current
                        : await Permissions.RequestAsync<PrinterBluetoothPermission>().ConfigureAwait(false);
                })
                .ConfigureAwait(false);

            _permissionBlock = status == PermissionStatus.Granted
                ? null
                : OperatingSystem.IsAndroidVersionAtLeast(31)
                    ? "Bluetooth permission is off · grant Nearby devices in Android settings, then scan again"
                    : "Location permission is off · Android needs it to search for Bluetooth devices below Android 12";
        }
        catch (Exception ex)
        {
            _permissionBlock = $"Could not check Bluetooth permission · {ex.Message}";
        }
    }

    /// <summary>
    /// The permission set for this API level, declared rather than guessed.
    /// <c>isRuntime</c> false means the manifest declaration is the whole of it and no
    /// dialog is raised, which is what the pre-31 Bluetooth pair is.
    /// </summary>
    private sealed class PrinterBluetoothPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            OperatingSystem.IsAndroidVersionAtLeast(31)
                ? new[]
                {
                    (Android.Manifest.Permission.BluetoothConnect, true),
                    (Android.Manifest.Permission.BluetoothScan, true)
                }
                : new[]
                {
                    (Android.Manifest.Permission.Bluetooth, false),
                    (Android.Manifest.Permission.BluetoothAdmin, false),
                    (OperatingSystem.IsAndroidVersionAtLeast(29)
                        ? Android.Manifest.Permission.AccessFineLocation
                        : Android.Manifest.Permission.AccessCoarseLocation, true)
                };
    }

    /// <summary>
    /// One <c>ACTION_FOUND</c> broadcast is one device. The receiver exists only for
    /// the length of an inquiry and is unregistered in the inquiry's finally block.
    /// </summary>
    private sealed class DeviceFoundReceiver(Action<BluetoothDevice> onFound) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action != BluetoothDevice.ActionFound)
            {
                return;
            }

            BluetoothDevice? device = OperatingSystem.IsAndroidVersionAtLeast(33)
                ? intent.GetParcelableExtra(BluetoothDevice.ExtraDevice, Java.Lang.Class.FromType(typeof(BluetoothDevice))) as BluetoothDevice
                : intent.GetParcelableExtra(BluetoothDevice.ExtraDevice) as BluetoothDevice;

            if (device is not null)
            {
                onFound(device);
            }
        }
    }

    /// <summary>
    /// One open RFCOMM socket. Disposed after every job — a socket held across a shift
    /// is how a terminal ends up unable to reconnect after somebody power-cycles the
    /// printer.
    /// </summary>
    private sealed class BluetoothPrinterConnection(BluetoothSocket socket) : IPrinterConnection
    {
        public async Task WriteAsync(byte[] payload, CancellationToken cancellationToken = default)
        {
            var stream = socket.OutputStream
                ?? throw new InvalidOperationException("The printer connection has no output stream");

            // Write and flush on the pool. The Java stream underneath is synchronous
            // whatever this side calls it, so awaiting WriteAsync on it would still
            // block the calling thread.
            await Task.Run(
                () =>
                {
                    stream.Write(payload, 0, payload.Length);
                    stream.Flush();
                },
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Listen for an Automatic Status Back block, and give up quietly.
        ///
        /// A Java input stream read blocks until bytes arrive or the socket closes —
        /// there is no "is anything waiting" to ask — so the read races a delay. If the
        /// delay wins, this returns empty and the orphaned read ends when the socket is
        /// disposed a moment later. Hearing nothing is a normal outcome the caller
        /// records as an unread status rather than as health.
        /// </summary>
        public async Task<byte[]> ReadAsync(TimeSpan wait, CancellationToken cancellationToken = default)
        {
            var stream = socket.InputStream;
            if (stream is null)
            {
                return Array.Empty<byte>();
            }

            var buffer = new byte[64];
            var read = Task.Run(
                () =>
                {
                    try
                    {
                        return stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (Exception)
                    {
                        return 0;
                    }
                },
                CancellationToken.None);

            var finished = await Task.WhenAny(read, Task.Delay(wait, cancellationToken)).ConfigureAwait(false);
            if (finished != read)
            {
                return Array.Empty<byte>();
            }

            var count = await read.ConfigureAwait(false);
            return count <= 0 ? Array.Empty<byte>() : buffer[..count];
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                socket.Close();
            }
            catch (Exception)
            {
            }

            socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }
#else
    public bool IsAvailable(out string? reason)
    {
        reason = "This build has no Bluetooth transport.";
        return false;
    }

    public Task<IReadOnlyList<PrinterDevice>> DiscoverAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PrinterDevice>>(Array.Empty<PrinterDevice>());

    public Task<IPrinterConnection> ConnectAsync(string deviceId, CancellationToken cancellationToken = default) =>
        throw new PlatformNotSupportedException("Bluetooth printing is implemented for Android only.");
#endif
}
