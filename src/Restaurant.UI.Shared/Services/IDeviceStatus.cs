namespace Restaurant.UI.Shared.Services;

/// <summary>
/// What the terminal shell can learn about the device it is running on: how much
/// charge is left, and whether there is a network.
///
/// Handbook §12 · "Reading the device from a shared component". This library holds
/// the terminal shell and every screen both hosts render, and it deliberately has
/// no MAUI reference — a component that referenced MAUI could not be stood up in
/// the back office's development preview at all. So it cannot call
/// <c>Battery.Default</c> or <c>Connectivity.Default</c>, and instead it owns the
/// question and lets each host answer it: <c>Restaurant.Mobile</c> registers the
/// implementation that reads MAUI Essentials, the back office registers
/// <see cref="UnknownDeviceStatus"/>. The dependency points from the host to this
/// library and never the other way.
///
/// The shell needs this at all because the terminal hides Android's status bar
/// (Part II-A · Handheld, "The host's system bars"). That bar carried the clock,
/// the signal and the battery; the clock was already in the top bar and the other
/// two had nowhere else to go, so the top bar took them on.
/// </summary>
public interface IDeviceStatus
{
    /// <summary>
    /// Remaining charge from 0 to 1, or null when this host cannot read a battery.
    ///
    /// Nullable rather than defaulting to 0, because §12 makes the distinction
    /// load-bearing: a host that cannot read a battery has not read a flat one.
    /// The null is what stops the development preview drawing a plausible dial
    /// over nothing.
    /// </summary>
    double? BatteryLevel { get; }

    /// <summary>
    /// True when the device has a network path, false when it has none, null when
    /// this host cannot tell.
    ///
    /// "Online" here means the terminal has a network, not that it can reach the
    /// public internet: the API this product talks to sits on the venue's own LAN,
    /// so a Wi-Fi link with no route out is still a working terminal.
    /// </summary>
    bool? IsOnline { get; }

    /// <summary>
    /// Raised when either reading changes. The shell re-renders off this rather
    /// than polling; a host with nothing to report never raises it.
    /// </summary>
    event EventHandler? Changed;
}

/// <summary>
/// The answer for a host with no device to read: both readings null, and no change
/// ever announced.
///
/// It is named for what it is rather than for the one host that registers it today.
/// §12 puts it beside the interface because it is this library's own answer for any
/// host without a device — the back office's preview now, a WASM build or a test
/// later — rather than a back-office detail.
///
/// This is not a stand-in for a battery. It is the reason there is no stand-in for
/// a battery: every consumer of <see cref="IDeviceStatus"/> has to handle null, and
/// the one that renders the shell's top bar handles it by drawing the absence of a
/// reading in the blocked treatment instead of a number nobody measured.
/// </summary>
public sealed class UnknownDeviceStatus : IDeviceStatus
{
    public double? BatteryLevel => null;

    public bool? IsOnline => null;

    /// <summary>Never raised. The accessors are written out so the compiler does not
    /// warn about an event field nothing assigns.</summary>
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}
