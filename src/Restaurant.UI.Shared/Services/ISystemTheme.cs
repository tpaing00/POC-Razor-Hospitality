namespace Restaurant.UI.Shared.Services;

/// <summary>
/// The two themes §11's theming contract defines. Light is the absence of
/// <c>data-theme</c> on <c>&lt;html&gt;</c>; dark is <c>data-theme="dark"</c>.
/// </summary>
public enum UposTheme
{
    /// <summary>No <c>data-theme</c> attribute.</summary>
    Light,

    /// <summary><c>data-theme="dark"</c>.</summary>
    Dark
}

/// <summary>
/// What the terminal shell can learn about the theme the operating system is set
/// to, and when that setting moves.
///
/// Handbook §12 · "Reading the device from a shared component", and the second
/// question asked in that shape. This library holds the terminal shell and every
/// screen both hosts render, and it deliberately has no MAUI reference — a
/// component that referenced MAUI could not be stood up in the back office's
/// development preview at all. So it cannot read
/// <c>Application.Current.RequestedTheme</c>, and instead it owns the question and
/// lets each host answer it: <c>Restaurant.Mobile</c> registers the implementation
/// that reads MAUI's application theme, the back office registers
/// <see cref="UnknownSystemTheme"/>. The dependency points from the host to this
/// library and never the other way. It is the shape <see cref="IDeviceStatus"/>
/// already uses, deliberately — a second style for the same problem would be a
/// second thing to learn.
///
/// Part II-A · Order entry · Dark: the terminal follows the OS and offers no
/// control of its own, so this interface is read-only. There is no setter here
/// because there is no toggle on the terminal to call one.
/// </summary>
public interface ISystemTheme
{
    /// <summary>
    /// The theme the OS is set to, or null when this host cannot tell.
    ///
    /// Nullable rather than defaulting to <see cref="UposTheme.Light"/>, because
    /// §12 makes the distinction load-bearing here exactly as it does for a
    /// battery: a host that cannot read a system theme has not read a light one.
    /// The null is what stops the back office's preview stamping
    /// <c>data-theme</c> over the theme the rail's own toggle just set.
    /// </summary>
    UposTheme? Theme { get; }

    /// <summary>
    /// Raised when the OS theme changes. The shell re-applies off this rather than
    /// reading once at startup, because a venue that turns the lights down at six
    /// does not restart its terminals; a host with nothing to report never raises
    /// it.
    /// </summary>
    event EventHandler? Changed;
}

/// <summary>
/// The answer for a host that has no system theme to read: null, and no change
/// ever announced.
///
/// It is named for what it is rather than for the one host that registers it
/// today, and it sits beside the interface because it is this library's own answer
/// for any such host — the back office's preview now, a WASM build or a test
/// later.
///
/// In the back office this is not a shortfall, it is the correct answer. That host
/// already has a theme control in its rail, and the terminal preview is a frame
/// inside its page: a preview that stamped the OS's theme onto
/// <c>&lt;html&gt;</c> would fight the toggle sitting eight inches to its left,
/// and would flip the whole back office around it. Answering null leaves the
/// attribute to the rail, which means the preview shows the terminal in whichever
/// theme the person working on it has chosen.
/// </summary>
public sealed class UnknownSystemTheme : ISystemTheme
{
    public UposTheme? Theme => null;

    /// <summary>Never raised. The accessors are written out so the compiler does not
    /// warn about an event field nothing assigns.</summary>
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}
