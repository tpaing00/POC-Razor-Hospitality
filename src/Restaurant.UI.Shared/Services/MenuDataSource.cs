using Microsoft.Extensions.Logging;
using Restaurant.Shared.Models.Dtos;

namespace Restaurant.UI.Shared.Services;

/// <summary>
/// The Menu manager's only data dependency. It is a real API client first: every read and
/// write goes to <see cref="RestaurantApiService"/>. When the API cannot be reached at all,
/// it falls back to <see cref="SeedMenuData"/> so the screen still renders for design work —
/// and reports that through <see cref="IsLive"/> so the page can say so out loud rather than
/// passing a mock off as a working integration.
///
/// Contract:
/// <list type="bullet">
///   <item><description><see cref="GetAllAsync"/> returns a snapshot — a fresh list of fresh
///   DTOs. Mutating it changes nothing; go through <see cref="CreateAsync"/>,
///   <see cref="UpdateAsync"/> and <see cref="DeleteAsync"/>.</description></item>
///   <item><description>A write that reaches the API leaves the internal list alone; the
///   caller re-reads. A write that cannot reach the API is applied to the internal list, so
///   the next read reflects it and the screen stays coherent for review.</description></item>
///   <item><description><see cref="IsLive"/> describes <em>the data the caller is holding</em>,
///   not the last call. It is false until a successful <see cref="GetAllAsync"/> has put API
///   data in the caller's hands, and false again the moment any call stops reaching the API.
///   A write succeeding while the seed is still on screen does <em>not</em> make it true — only
///   a successful read, which replaces the seed, can.</description></item>
///   <item><description>An item created while the API was unreachable is a local artefact. It
///   disappears the moment a successful <see cref="GetAllAsync"/> replaces the list, because
///   the API is the source of truth and it was never sent there. That happens in the same
///   render as <see cref="IsLive"/> becoming true, so the "seed data" notice clears at exactly
///   the moment the invented rows do.</description></item>
/// </list>
///
/// Registered scoped, so the in-memory list lives for one Blazor circuit and no longer.
/// It is <em>not</em> safe for concurrent calls: the internal list is reassigned wholesale by a
/// successful read, so a <see cref="GetAllAsync"/> that completes after an offline write lands
/// will discard that write. Blazor's circuit dispatches component work one call at a time, so
/// awaiting each call before starting the next — which is what the Menu manager does — is
/// enough; do not fire these off in parallel.
/// </summary>
public class MenuDataSource
{
    private readonly RestaurantApiService _api;
    private readonly ILogger<MenuDataSource> _logger;

    /// <summary>The list the fallback path serves and mutates. Only meaningful when the API
    /// is unreachable; a successful read replaces it wholesale.</summary>
    private List<MenuItemDto> _items = new();

    /// <summary>True once <see cref="_items"/> holds either loaded data or the seed, so an
    /// outage after a successful load keeps the real rows instead of resetting to seed.</summary>
    private bool _hasLocalData;

    /// <summary>Whether the most recent call got through to the API.</summary>
    private bool _lastCallReachedApi;

    /// <summary>Whether the rows the caller is holding are invented. Set when the seed is
    /// loaded and cleared only by a successful <see cref="GetAllAsync"/> — a write getting
    /// through does not put real rows on screen, so it must not clear this.</summary>
    private bool _showingSeed;

    public MenuDataSource(RestaurantApiService api, ILogger<MenuDataSource> logger)
    {
        _api = api;
        _logger = logger;
    }

    /// <summary>
    /// False whenever the data the caller is holding did not come from the API — including
    /// before the first call, and including after a write succeeds while the seed is still on
    /// screen. Both halves must hold: the last call reached the API, <em>and</em> the rows are
    /// not the seed. Read it after awaiting the call whose result is on screen.
    /// </summary>
    public bool IsLive => _lastCallReachedApi && !_showingSeed;

    /// <summary>
    /// The whole menu, 86'd items included, so the back office can restore them.
    /// </summary>
    public async Task<List<MenuItemDto>> GetAllAsync()
    {
        try
        {
            _items = await _api.GetMenuItemsAsync(includeUnavailable: true);
            _hasLocalData = true;
            _lastCallReachedApi = true;

            // The only thing that puts real rows in the caller's hands, so the only thing
            // entitled to clear the seed flag. Anything invented offline is gone with it.
            _showingSeed = false;
        }
        catch (Exception ex) when (IsUnreachable(ex))
        {
            LogFallback(ex, "load the menu");
            FallBackToLocalData();
        }

        return _items.Select(Copy).ToList();
    }

    public async Task<MenuItemDto?> CreateAsync(MenuItemDto item)
    {
        try
        {
            var created = await _api.CreateMenuItemAsync(item);
            _lastCallReachedApi = true;
            return created;
        }
        catch (Exception ex) when (IsUnreachable(ex))
        {
            LogFallback(ex, "create a menu item");
            FallBackToLocalData();

            var local = Copy(item);
            local.Id = NextLocalId();
            _items.Add(local);
            return Copy(local);
        }
    }

    public async Task UpdateAsync(MenuItemDto item)
    {
        try
        {
            await _api.UpdateMenuItemAsync(item);
            _lastCallReachedApi = true;
        }
        catch (Exception ex) when (IsUnreachable(ex))
        {
            LogFallback(ex, $"update menu item {item.Id}");
            FallBackToLocalData();

            var index = _items.FindIndex(i => i.Id == item.Id);
            if (index >= 0)
                _items[index] = Copy(item);
            else
                LogLocalMiss("update", item.Id);
        }
    }

    public async Task DeleteAsync(int id)
    {
        try
        {
            await _api.DeleteMenuItemAsync(id);
            _lastCallReachedApi = true;
        }
        catch (Exception ex) when (IsUnreachable(ex))
        {
            LogFallback(ex, $"delete menu item {id}");
            FallBackToLocalData();

            if (_items.RemoveAll(i => i.Id == id) == 0)
                LogLocalMiss("delete", id);
        }
    }

    /// <summary>
    /// Only a connection-level failure justifies the seed fallback. An HTTP status — a 404, a
    /// 500 from a live API — is a real error the developer needs to see, so it propagates.
    /// <see cref="HttpRequestException.StatusCode"/> is null exactly when no response was
    /// received (DNS, connect, TLS, socket); when the server answered, it carries that answer.
    /// A timeout surfaces as <see cref="TaskCanceledException"/>, and since none of these
    /// calls take a caller cancellation token, a cancellation can only be the HttpClient
    /// timeout. Everything else — malformed JSON, a bad base address — is left to throw.
    /// </summary>
    private static bool IsUnreachable(Exception ex) => ex switch
    {
        HttpRequestException http => http.StatusCode is null,
        TaskCanceledException => true,
        _ => false
    };

    /// <summary>
    /// Drops to local data and records that the screen is no longer looking at the API.
    /// Seeds only if nothing has been loaded yet — a successful load followed by an outage
    /// keeps the real rows rather than silently swapping them for invented ones, and a live
    /// menu that is genuinely empty stays empty.
    /// </summary>
    private void FallBackToLocalData()
    {
        _lastCallReachedApi = false;

        if (!_hasLocalData)
        {
            _items = SeedMenuData.Create();
            _hasLocalData = true;
            _showingSeed = true;
        }
    }

    private int NextLocalId() => _items.Count == 0 ? 1 : _items.Max(i => i.Id) + 1;

    /// <summary>An offline write that matched nothing changes nothing. Silence would leave the
    /// screen looking as if it had worked, so say so.</summary>
    private void LogLocalMiss(string operation, int id) =>
        _logger.LogWarning(
            "Offline {Operation} of menu item {MenuItemId} matched no local row, so nothing " +
            "changed. The list may be stale.", operation, id);

    private void LogFallback(Exception ex, string attempted) =>
        _logger.LogWarning(ex,
            "Menu API unreachable while trying to {Attempted}. Serving local data; the screen " +
            "is not showing live data.", attempted);

    private static MenuItemDto Copy(MenuItemDto item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Description = item.Description,
        Price = item.Price,
        Category = item.Category,
        IsAvailable = item.IsAvailable,
        ImageUrl = item.ImageUrl
    };
}
