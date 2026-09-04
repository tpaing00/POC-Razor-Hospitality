#nullable enable

using Restaurant.UI.Shared.Services;

namespace Restaurant.Mobile.Services;

/// <summary>
/// <see cref="ISystemTheme"/> read off MAUI's application theme, which is the host
/// half of handbook §12's "Reading the device from a shared component" — the same
/// division <see cref="MauiDeviceStatus"/> makes for the battery and the network.
///
/// <c>Restaurant.UI.Shared</c> has no MAUI reference and cannot reach
/// <c>Application.Current</c> at all; this project can, so the terminal follows
/// Android's own light/dark setting here and honestly answers null in the back
/// office's preview.
///
/// <c>Application.RequestedTheme</c> is MAUI's surfacing of the platform setting:
/// on Android it is the uiMode night qualifier, which is what the system Display
/// settings and any MDM policy move. <c>UserAppTheme</c> is deliberately not read.
/// That property is the app's own override of the platform value, the terminal
/// sets no override (Part II-A · Order entry · Dark rules out a manual control),
/// and reading the override rather than the platform would make this class report
/// its own answer back to itself.
/// </summary>
public sealed class MauiSystemTheme : ISystemTheme, IDisposable
{
    private bool _disposed;

    public MauiSystemTheme()
    {
        // Pushed rather than polled, exactly as the battery and the network are.
        // Android raises this when the night mode qualifier changes, which is what
        // makes the terminal follow a venue that turns the lights down mid-service
        // rather than only one that restarts.
        try
        {
            if (Application.Current is { } app)
            {
                app.RequestedThemeChanged += OnRequestedThemeChanged;
            }
        }
        catch (Exception)
        {
            // A host with no MAUI application object. The reading below answers
            // null and the shell leaves the attribute alone; there is nothing to
            // subscribe to and nothing to report.
        }
    }

    /// <summary>
    /// The OS theme, or null when MAUI reports <c>Unspecified</c> or there is no
    /// application to ask.
    ///
    /// <c>Unspecified</c> becomes null rather than light: it is MAUI saying it does
    /// not know, which §12 rules is not the same as saying light, and answering
    /// light would have the shell strip a <c>data-theme</c> it never set.
    /// </summary>
    public UposTheme? Theme
    {
        get
        {
            try
            {
                return Application.Current?.RequestedTheme switch
                {
                    AppTheme.Dark => UposTheme.Dark,
                    AppTheme.Light => UposTheme.Light,
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

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e) =>
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
            if (Application.Current is { } app)
            {
                app.RequestedThemeChanged -= OnRequestedThemeChanged;
            }
        }
        catch (Exception)
        {
        }
    }
}
