using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;

namespace Restaurant.Mobile
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        /// <summary>
        /// Hand the window's system-bar insets back to the framework, so the
        /// BlazorWebView is laid out between Android's status bar and its
        /// navigation bar rather than underneath them.
        ///
        /// .NET MAUI 10 puts every Android window into edge-to-edge:
        /// Microsoft.Maui calls WindowCompat.SetDecorFitsSystemWindows(window,
        /// false) during the activity's own OnCreate, on every API level. The
        /// theme's maui_edgetoedge_optout is not the counterweight — the
        /// generated styles.xml says in a comment that only v35+ reads it, and
        /// this device is API 28 — so on the Memor 20 the WebView filled the
        /// whole 1080x2160 panel and the terminal shell ran under both bars: the
        /// 26px mark clipped from above, the lower half of the bottom nav under
        /// the navigation bar.
        ///
        /// It cannot be answered in CSS. env(safe-area-inset-*) reports zero in
        /// this WebView because Android exposes display-cutout insets there and
        /// not system-bar insets, so a padding rule off those four values is a
        /// rule that reads as a fix and adds nothing.
        ///
        /// SetDecorFitsSystemWindows(true) is the back-compatible switch for
        /// exactly the call MAUI makes, and it is called after base.OnCreate so
        /// it lands after MAUI's. It is preferred over keeping edge-to-edge and
        /// re-applying the insets through an OnApplyWindowInsetsListener because
        /// it needs no listener, no plumbing of pixel values into CSS custom
        /// properties, and no second source of truth for a height the platform
        /// already knows: the decor view goes back to fitting the bars, the
        /// content view is sized between them, and the shell measures 100% of a
        /// box that is already correct.
        ///
        /// The bars themselves stay on the design's colours — colorPrimaryDark
        /// #171C22 is the dark panel, and it is what the status bar fills with
        /// once the window stops drawing behind it.
        /// </summary>
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            if (Window is not null)
            {
                WindowCompat.SetDecorFitsSystemWindows(Window, true);
            }
        }
    }
}
