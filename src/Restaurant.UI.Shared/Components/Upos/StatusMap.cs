using Restaurant.Shared.Models;

namespace Restaurant.UI.Shared.Components.Upos;

/// <summary>
/// Handbook §4 in one place: the <see cref="OrderStatus"/> to hue and word map, the
/// chip modifier per hue, the token names per hue, and the lateness derivation.
/// <para>
/// Late is derived, never stored. Nothing upstream carries an <c>IsLate</c> field and
/// no endpoint returns one, so every component that can show late takes an elapsed
/// value (or a timestamp) plus the threshold and calls <see cref="IsLate(TimeSpan?, TimeSpan?)"/>
/// on render.
/// </para>
/// </summary>
public static class StatusMap
{
    /// <summary>§4's map: Pending/Confirmed new-blue, Preparing fired-orange,
    /// Ready/Served/Completed ready-green, Cancelled late-red.</summary>
    public static StatusTone ToneFor(OrderStatus status) => status switch
    {
        OrderStatus.Pending or OrderStatus.Confirmed => StatusTone.New,
        OrderStatus.Preparing => StatusTone.Fired,
        OrderStatus.Ready or OrderStatus.Served or OrderStatus.Completed => StatusTone.Ready,
        OrderStatus.Cancelled => StatusTone.Late,
        _ => StatusTone.New
    };

    /// <summary>§4's chip label: PENDING · SENT · FIRED · READY · SERVED · PAID · VOID.</summary>
    public static string LabelFor(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "PENDING",
        OrderStatus.Confirmed => "SENT",
        OrderStatus.Preparing => "FIRED",
        OrderStatus.Ready => "READY",
        OrderStatus.Served => "SERVED",
        OrderStatus.Completed => "PAID",
        OrderStatus.Cancelled => "VOID",
        _ => status.ToString().ToUpperInvariant()
    };

    /// <summary>The kit modifier that fills a chip with this hue.</summary>
    public static string ChipClass(StatusTone tone) => tone switch
    {
        StatusTone.Fired => "u-chip-status--fired",
        StatusTone.Late => "u-chip-status--late",
        StatusTone.Ready => "u-chip-status--ready",
        _ => "u-chip-status--new"
    };

    /// <summary>The fill token, for chips, dots, borders and bars (§4).</summary>
    public static string FillToken(StatusTone tone) => tone switch
    {
        StatusTone.Fired => "var(--upos-status-fired)",
        StatusTone.Late => "var(--upos-status-late)",
        StatusTone.Ready => "var(--upos-status-ready)",
        _ => "var(--upos-status-new)"
    };

    /// <summary>The text token, for words. §4: the two sets are not interchangeable.</summary>
    public static string TextToken(StatusTone tone) => tone switch
    {
        StatusTone.Fired => "var(--upos-status-fired-text)",
        StatusTone.Late => "var(--upos-status-late-text)",
        StatusTone.Ready => "var(--upos-status-ready-text)",
        _ => "var(--upos-status-new)"
    };

    /// <summary>Late holds while elapsed exceeds the station or channel threshold.
    /// Both halves are parameters; neither is read back from a model.</summary>
    public static bool IsLate(TimeSpan? elapsed, TimeSpan? lateAfter) =>
        elapsed.HasValue && lateAfter.HasValue && elapsed.Value > lateAfter.Value;

    /// <summary>The timestamp form: <c>now - since &gt; lateAfter</c> (§4).</summary>
    public static bool IsLate(DateTime since, DateTime now, TimeSpan lateAfter) =>
        now - since > lateAfter;
}
