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
        /// Take the whole screen: hide Android's status bar and navigation bar, and
        /// keep them hidden.
        ///
        /// Handbook Part II-A · Handheld, "The host's system bars". The reason is
        /// operational and not cosmetic — the navigation bar's back and home
        /// controls are a way out of the app, and a terminal a member of staff can
        /// leave mid-order is a terminal that loses a check. Hiding the bars is the
        /// only thing that removes the control; trapping the back press covers one
        /// door and leaves the other open.
        ///
        /// This replaces the SetDecorFitsSystemWindows(window, true) that used to
        /// stand here, and the two are not independent settings that could both be
        /// applied. Below API 30 WindowCompat.SetDecorFitsSystemWindows is not a
        /// distinct API: it clears (for true) or sets (for false) exactly three bits
        /// on the decor view — SYSTEM_UI_FLAG_LAYOUT_STABLE,
        /// SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION and SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
        /// — and those are the same three bits WindowInsetsControllerCompat sets when
        /// its SystemBarsBehavior is BehaviorShowTransientBarsBySwipe. Calling both
        /// leaves the outcome decided by call order, which is not a design. So this
        /// states the edge-to-edge layout it wants once, then hides the bars.
        ///
        /// The old call is not so much reverted as made unnecessary. It existed to
        /// stop the WebView being laid out underneath two bars that were taking
        /// screen; there are no longer two bars taking screen. Its premise is gone.
        /// With the bars hidden the shell's height:100% is the whole 1080x2160 panel
        /// again — 393x785 CSS px at 440dpi — and this 785 is the device, where the
        /// 785 before the original fix was a defect: that one was the shell
        /// measuring 72px of chrome it did not own and being clipped at both ends.
        ///
        /// A transient bar does not clip the shell, because it does not re-lay-out
        /// anything. BehaviorShowTransientBarsBySwipe is sticky immersive: an edge
        /// swipe brings a bar back as a translucent overlay for a few seconds and
        /// then takes it away, and the system dispatches no new window insets for it,
        /// because a bar that is about to leave is not a bar the layout should be
        /// built around. The shell keeps its 393x785 box for the whole episode; the
        /// bar floats over the mark or the foot of the nav and retreats leaving both
        /// intact. Momentarily covered is not clipped.
        /// </summary>
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // After base.OnCreate, so it lands after the call Microsoft.Maui makes
            // during the activity's own OnCreate.
            TakeTheWholeScreen();
        }

        /// <summary>
        /// Re-hide the bars whenever the window comes back to the front. A system
        /// dialog, the notification shade or the keyboard can restore them and leave
        /// them restored, and a terminal that quietly grows a back button after an
        /// interruption is the failure this whole arrangement exists to prevent.
        /// </summary>
        public override void OnWindowFocusChanged(bool hasFocus)
        {
            base.OnWindowFocusChanged(hasFocus);

            if (hasFocus)
            {
                TakeTheWholeScreen();
            }
        }

        private void TakeTheWholeScreen()
        {
            if (Window is null)
            {
                return;
            }

            // Lay the content out edge to edge. This is what MAUI itself does, and
            // with the bars hidden it is what makes the WebView the full panel.
            WindowCompat.SetDecorFitsSystemWindows(Window, false);

            var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);

            if (controller is null)
            {
                return;
            }

            // Sticky immersive. Set the behaviour before hiding, so the first hide
            // already carries it.
            controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
            controller.Hide(WindowInsetsCompat.Type.SystemBars());
        }
    }
}
