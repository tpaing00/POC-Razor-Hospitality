using Microsoft.Extensions.Logging;
using Restaurant.Shared.Models.Dtos;

namespace Restaurant.UI.Shared.Services;

/// <summary>
/// The Dashboard's only data dependency, and the read-only sibling of <see cref="MenuDataSource"/>
/// — same exception discipline, same <see cref="IsLive"/> semantics, same honesty contract. It is
/// a real API client first: <see cref="GetAsync"/> calls
/// <see cref="RestaurantApiService.GetDashboardAsync"/>. When the API cannot be reached at all it
/// falls back to <see cref="SeedDashboardData"/> so the screen still renders for design work — and
/// reports that through <see cref="IsLive"/> so the page can say so out loud rather than passing
/// invented figures off as a working integration.
///
/// Contract:
/// <list type="bullet">
///   <item><description><see cref="GetAsync"/> returns a snapshot — a fresh
///   <see cref="DashboardDto"/> with fresh lists. Mutating it changes nothing here. It never
///   returns null, and its lists are never null.</description></item>
///   <item><description><see cref="IsLive"/> describes <em>the figures the caller is holding</em>,
///   not the last call. It is false until a successful <see cref="GetAsync"/> has put API data in
///   the caller's hands, and false again the moment a call stops reaching the API. Read it after
///   awaiting the call whose result is on screen.</description></item>
///   <item><description>A failed refresh after a successful load keeps the last real figures and
///   drops <see cref="IsLive"/> to false. It does not substitute the seed for real data — the page
///   says "not live" rather than silently swapping in a different day's numbers.</description></item>
/// </list>
///
/// This screen is read-only, so <see cref="IsLive"/>'s two flags happen to move together today.
/// They are kept separate anyway, and the seed flag is cleared in exactly one place — the success
/// path of the read — so that if a write is ever added here it cannot flip <see cref="IsLive"/>
/// true while invented figures are still on screen. That is the bug
/// <see cref="MenuDataSource"/> was fixed for; the shape that prevents it is copied, not the
/// symptom.
///
/// Registered scoped, so the retained figures live for one Blazor circuit and no longer. Like
/// <see cref="MenuDataSource"/> it is not safe for concurrent calls — a successful read replaces
/// the retained value wholesale. Blazor's circuit dispatches component work one call at a time, so
/// awaiting each call before starting the next is enough.
/// </summary>
public class DashboardDataSource
{
    private readonly RestaurantApiService _api;
    private readonly ILogger<DashboardDataSource> _logger;

    /// <summary>The figures the fallback path serves. Only meaningful when the API is unreachable;
    /// a successful read replaces it wholesale.</summary>
    private DashboardDto _dashboard = new();

    /// <summary>True once <see cref="_dashboard"/> holds either loaded data or the seed, so an
    /// outage after a successful load keeps the real figures instead of resetting to seed.</summary>
    private bool _hasLocalData;

    /// <summary>Whether the most recent call got through to the API.</summary>
    private bool _lastCallReachedApi;

    /// <summary>Whether the figures the caller is holding are invented. Set when the seed is
    /// loaded and cleared only by a successful <see cref="GetAsync"/>.</summary>
    private bool _showingSeed;

    public DashboardDataSource(RestaurantApiService api, ILogger<DashboardDataSource> logger)
    {
        _api = api;
        _logger = logger;
    }

    /// <summary>
    /// False whenever the figures the caller is holding did not come from the API — including
    /// before the first call. Both halves must hold: the last call reached the API, <em>and</em>
    /// the figures are not the seed.
    /// </summary>
    public bool IsLive => _lastCallReachedApi && !_showingSeed;

    /// <summary>
    /// The Dashboard aggregate for a period. Omit both bounds for the API's default window, the
    /// current UTC day — which is a stand-in for a real business day, not one (GAP-13).
    /// </summary>
    public async Task<DashboardDto> GetAsync(DateTime? from = null, DateTime? to = null)
    {
        try
        {
            var loaded = await _api.GetDashboardAsync(from, to);

            // A 200 with a null body is a malformed response, not an outage. Serving the seed for
            // it would be the exact lie this class exists to prevent, and serving zeros would
            // claim the venue sold nothing. It is a bug, so it throws — same discipline that lets
            // a JsonException through.
            if (loaded is null)
                throw new InvalidOperationException(
                    "The dashboard endpoint answered successfully with an empty body.");

            _dashboard = loaded;
            _hasLocalData = true;
            _lastCallReachedApi = true;

            // The only thing that puts real figures in the caller's hands, so the only thing
            // entitled to clear the seed flag.
            _showingSeed = false;
        }
        catch (Exception ex) when (IsUnreachable(ex))
        {
            LogFallback(ex);
            FallBackToLocalData();
        }

        return Copy(_dashboard);
    }

    /// <summary>
    /// Only a connection-level failure justifies the seed fallback. An HTTP status — a 404, a 500
    /// from a live API — is a real error the developer needs to see, so it propagates.
    /// <see cref="HttpRequestException.StatusCode"/> is null exactly when no response was received
    /// (DNS, connect, TLS, socket); when the server answered, it carries that answer. A timeout
    /// surfaces as <see cref="TaskCanceledException"/>, and since this call takes no caller
    /// cancellation token, a cancellation can only be the HttpClient timeout — if a token
    /// parameter is ever added, this branch must be narrowed. Everything else — malformed JSON, a
    /// bad base address — is left to throw.
    /// </summary>
    private static bool IsUnreachable(Exception ex) => ex switch
    {
        HttpRequestException http => http.StatusCode is null,
        TaskCanceledException => true,
        _ => false
    };

    /// <summary>
    /// Drops to local data and records that the screen is no longer looking at the API. Seeds only
    /// if nothing has been loaded yet — a successful load followed by an outage keeps the real
    /// figures, and a live day with genuinely no sales stays at zero rather than being handed an
    /// invented service.
    /// </summary>
    private void FallBackToLocalData()
    {
        _lastCallReachedApi = false;

        if (!_hasLocalData)
        {
            _dashboard = SeedDashboardData.Create();
            _hasLocalData = true;
            _showingSeed = true;
        }
    }

    private void LogFallback(Exception ex) =>
        _logger.LogWarning(ex,
            "Reports API unreachable while trying to load the dashboard. Serving local data; the " +
            "screen is not showing live data.");

    /// <summary>A deep copy, so a page that sorts or trims the lists it was handed cannot corrupt
    /// what the fallback path serves next time.</summary>
    private static DashboardDto Copy(DashboardDto source) => new()
    {
        From = source.From,
        To = source.To,
        NetSales = source.NetSales,
        OrderCount = source.OrderCount,
        AverageTicket = source.AverageTicket,
        Dayparts = source.Dayparts
            .Select(d => new DaypartSliceDto
            {
                Name = d.Name,
                Sales = d.Sales,
                OrderCount = d.OrderCount
            })
            .ToList(),
        TopItems = source.TopItems
            .Select(t => new TopItemDto
            {
                MenuItemId = t.MenuItemId,
                Name = t.Name,
                QuantitySold = t.QuantitySold,
                Sales = t.Sales
            })
            .ToList()
    };
}
