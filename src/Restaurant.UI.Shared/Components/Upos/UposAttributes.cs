namespace Restaurant.UI.Shared.Components.Upos;

/// <summary>
/// Merges a component's own kit classes and inline geometry with whatever a call site
/// splats, so <c>@attributes</c> can carry <c>title</c>, <c>aria-*</c> and <c>data-*</c>
/// without a call site's <c>class</c> or <c>style</c> silently replacing the recipe.
/// Blazor renders duplicate attributes last-wins; splatting a merged dictionary is the
/// only way to keep <c>.u-btn</c> on the element while still letting a call site add to it.
/// </summary>
internal static class UposAttributes
{
    public static IReadOnlyDictionary<string, object> Merge(
        IReadOnlyDictionary<string, object>? extra,
        string cssClass,
        string? style = null)
    {
        var merged = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        if (extra is not null)
        {
            foreach (var pair in extra)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        var callSiteClass = merged.TryGetValue("class", out var c) ? c?.ToString() : null;
        merged["class"] = string.IsNullOrWhiteSpace(callSiteClass)
            ? cssClass
            : $"{cssClass} {callSiteClass}";

        var callSiteStyle = merged.TryGetValue("style", out var s) ? s?.ToString() : null;
        var combined = string.Join("; ", new[] { style, callSiteStyle }
            .Where(v => !string.IsNullOrWhiteSpace(v)));
        if (combined.Length > 0)
        {
            merged["style"] = combined;
        }

        return merged;
    }
}
