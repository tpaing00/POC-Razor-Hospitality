using Microsoft.AspNetCore.Components;

namespace Restaurant.UI.Shared.Components.Upos;

/// <summary>
/// One destination in <c>BottomNav</c> (and in the back office's rail, when SideNav
/// lands). Part III preamble: an enum or record named in a props line that is not one
/// of the POC's types ships with the component and describes rendering, not data.
/// </summary>
/// <param name="Key">The destination's identity, matched against
/// <c>BottomNav.Active</c> and reported by <c>OnNavigate</c>.</param>
/// <param name="Label">The word under the icon. §10: destinations are the noun of the
/// thing, rendered in the 10px label class.</param>
/// <param name="Icon">A 17px inline SVG at stroke-width 2 (§9). Never a glyph.</param>
public sealed record NavItem(string Key, string Label, RenderFragment? Icon = null)
{
    /// <summary>
    /// Whether the destination can be reached. Part III's props line for
    /// <c>BottomNav</c> carries Key, Label and Icon only; this fourth field is here
    /// because four of the five terminal destinations are not built in this pass, and
    /// a nav item that answers a tap by doing nothing is the one thing worse than one
    /// that says it is not there. The back-office rail already renders its unbuilt
    /// destinations this way (<c>NavMenu.razor</c>).
    /// </summary>
    public bool Enabled { get; init; } = true;
}
