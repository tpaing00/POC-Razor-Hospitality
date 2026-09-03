#nullable enable

using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
using Restaurant.UI.Shared.Services;

namespace Restaurant.Mobile.Services;

/// <summary>
/// <see cref="IDeviceStatus"/> read off MAUI Essentials, which is the host half of
/// handbook §12's "Reading the device from a shared component".
///
/// <c>Restaurant.UI.Shared</c> has no MAUI reference and cannot reach Essentials at
/// all; this project can, so the terminal shell's battery and connectivity
/// indicators are real here and honestly blank in the back office's preview.
///
/// Essentials is inconsistent about the accessor and it is worth naming rather than
/// rediscovering: the battery facade is <c>Battery.Default</c> and the connectivity
/// facade is <c>Connectivity.Current</c>.
/// </summary>
public sealed class MauiDeviceStatus : IDeviceStatus, IDisposable
{
    private bool _disposed;

    public MauiDeviceStatus()
    {
        // Both readings are pushed rather than polled. Essentials raises these on
        // the platform's own callbacks, so the shell re-renders when the charge or
        // the connection actually moves and at no other time.
        try
        {
            Battery.Default.BatteryInfoChanged += OnBatteryChanged;
        }
        catch (Exception)
        {
            // A platform with no battery API. The reading below returns null and the
            // shell draws the absence of a reading; there is nothing to subscribe to
            // and nothing to report.
        }

        try
        {
            Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Charge from 0 to 1. Essentials answers -1 when it does not know, and a
    /// platform without the feature throws; both become null rather than a number,
    /// because §12 rules that a reading nobody took is never rendered as data.
    /// </summary>
    public double? BatteryLevel
    {
        get
        {
            try
            {
                var level = Battery.Default.ChargeLevel;
                return level is >= 0 and <= 1 ? level : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Whether the device has a network path at all.
    ///
    /// <c>Local</c> and <c>ConstrainedInternet</c> count as online alongside
    /// <c>Internet</c>, because this product's API sits on the venue's LAN (see the
    /// base address in <c>MauiProgram</c>): a Wi-Fi link that Android has not
    /// validated a route to the public internet on is still a terminal that can
    /// reach its own server, and calling that offline would put a red pill on a
    /// working bar. <c>None</c> is offline. <c>Unknown</c> is null — Essentials
    /// saying it does not know, which is not the same as saying no.
    /// </summary>
    public bool? IsOnline
    {
        get
        {
            try
            {
                return Connectivity.Current.NetworkAccess switch
                {
                    NetworkAccess.Internet or NetworkAccess.Local or NetworkAccess.ConstrainedInternet => true,
                    NetworkAccess.None => false,
                    _ => null
                };
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public event EventHandler? Changed;

    private void OnBatteryChanged(object? sender, BatteryInfoChangedEventArgs e) =>
        Changed?.Invoke(this, EventArgs.Empty);

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) =>
        Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            Battery.Default.BatteryInfoChanged -= OnBatteryChanged;
        }
        catch (Exception)
        {
        }

        try
        {
            Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        }
        catch (Exception)
        {
        }
    }
}
