namespace Restaurant.UI.Shared.Components.Upos;

// Handbook Part III preamble: "An enum or record named in a props line that is not
// one of [the POC's types] ships with the component and describes rendering, not
// data." These four are exactly that — no component invents a DTO.

/// <summary>The three button treatments the kit ships (<c>.u-btn--*</c>).</summary>
public enum ButtonVariant
{
    Primary,
    Secondary,
    Ghost
}

/// <summary>
/// The four status hues of handbook §4, for a state that is not an
/// <see cref="Restaurant.Shared.Models.OrderStatus"/> — an 86'd menu item is a
/// menu-item state, not an order state, and still belongs in late-red.
/// </summary>
public enum StatusTone
{
    New,
    Fired,
    Late,
    Ready
}

/// <summary>
/// Part II-B's ruling on <c>StatCard</c>: the kit recipe is right for a card on
/// the ground and wrong for one inside a white panel, where §2's ladder puts
/// blocks on <c>--upos-surface-inset</c> with no shadow.
/// </summary>
public enum SurfaceFill
{
    Ground,
    Panel
}

/// <summary>The three shapes <c>MenuItemCard</c> takes across the specs.</summary>
public enum CardLayout
{
    Tile,
    GridCard,
    ListRow
}

/// <summary>
/// Terminal is Part I's type roles at 1x; Kiosk is the same roles at x1.4 and
/// lifts the whole element to <c>--upos-touch-kiosk</c> (Guest-facing rules).
/// </summary>
public enum TypeScale
{
    Terminal,
    Kiosk
}
