using System.Net.Http.Json;
using Restaurant.Shared.Models;
using Restaurant.Shared.Models.Dtos;

namespace Restaurant.UI.Shared.Services;

public class RestaurantApiService
{
    private readonly HttpClient _httpClient;

    public RestaurantApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    // Menu Items
    public async Task<List<MenuItemDto>> GetMenuItemsAsync()
    {
        return await _httpClient.GetFromJsonAsync<List<MenuItemDto>>("api/menu") ?? new();
    }

    public async Task<List<MenuItemDto>> GetMenuItemsAsync(bool includeUnavailable)
    {
        var url = includeUnavailable ? "api/menu?includeUnavailable=true" : "api/menu";
        return await _httpClient.GetFromJsonAsync<List<MenuItemDto>>(url) ?? new();
    }

    public async Task<MenuItemDto?> GetMenuItemAsync(int id)
    {
        return await _httpClient.GetFromJsonAsync<MenuItemDto>($"api/menu/{id}");
    }

    public async Task<MenuItemDto?> CreateMenuItemAsync(MenuItemDto item)
    {
        var response = await _httpClient.PostAsJsonAsync("api/menu", item);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MenuItemDto>();
    }

    public async Task UpdateMenuItemAsync(MenuItemDto item)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/menu/{item.Id}", item);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteMenuItemAsync(int id)
    {
        var response = await _httpClient.DeleteAsync($"api/menu/{id}");
        response.EnsureSuccessStatusCode();
    }

    // Tables
    public async Task<List<Table>> GetTablesAsync()
    {
        return await _httpClient.GetFromJsonAsync<List<Table>>("api/tables") ?? new();
    }

    public async Task<List<Table>> GetAvailableTablesAsync()
    {
        return await _httpClient.GetFromJsonAsync<List<Table>>("api/tables/available") ?? new();
    }

    // Orders
    public async Task<List<OrderDto>> GetOrdersAsync()
    {
        return await _httpClient.GetFromJsonAsync<List<OrderDto>>("api/orders") ?? new();
    }

    public async Task<OrderDto?> GetOrderAsync(int id)
    {
        return await _httpClient.GetFromJsonAsync<OrderDto>($"api/orders/{id}");
    }

    public async Task<OrderDto?> CreateOrderAsync(CreateOrderDto order)
    {
        var response = await _httpClient.PostAsJsonAsync("api/orders", order);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrderDto>();
    }

    public async Task UpdateOrderStatusAsync(int orderId, OrderStatus status)
    {
        var response = await _httpClient.PatchAsJsonAsync($"api/orders/{orderId}/status",
            new UpdateOrderStatusDto { Status = status });
        response.EnsureSuccessStatusCode();
    }

    // Printers · the venue's registry (handbook Part II-B · Printers)
    //
    // These are the venue's record of which printers it owns. None of them selects a
    // printer for anything: which printer a device prints to is that device's own
    // stored preference, and nothing here reaches it.
    //
    // The four write methods do NOT call EnsureSuccessStatusCode. The registry refuses
    // a write with one sentence naming the fault and the next move — a duplicate
    // address, a name that will not fit, an address that will not parse — and
    // EnsureSuccessStatusCode would throw that sentence away and leave the screen with
    // "Response status code does not indicate success: 409 (Conflict)", which tells a
    // person nothing they can act on.

    /// <summary>
    /// The venue's printers, ordered by what they are for.
    /// </summary>
    /// <param name="includeInactive">Include the ones marked out of service.</param>
    public async Task<List<PrinterDto>> GetPrintersAsync(bool includeInactive = false)
    {
        var url = includeInactive ? "api/printers?includeInactive=true" : "api/printers";
        return await _httpClient.GetFromJsonAsync<List<PrinterDto>>(url) ?? new();
    }

    public async Task<PrinterDto?> GetPrinterAsync(int id)
    {
        return await _httpClient.GetFromJsonAsync<PrinterDto>($"api/printers/{id}");
    }

    /// <summary>
    /// Register a printer. Returns the stored row, whose address is normalized and may
    /// therefore differ from the one sent — <c>192.168.1.50</c> comes back as
    /// <c>192.168.1.50:9100</c>.
    /// </summary>
    /// <exception cref="PrinterRegistryException">The registry refused the write. The
    /// message is the server's own sentence and is safe to render.</exception>
    public async Task<PrinterDto?> CreatePrinterAsync(PrinterDto printer)
    {
        var response = await _httpClient.PostAsJsonAsync("api/printers", printer);
        await ThrowIfRefusedAsync(response);
        return await response.Content.ReadFromJsonAsync<PrinterDto>();
    }

    /// <inheritdoc cref="CreatePrinterAsync"/>
    public async Task<PrinterDto?> UpdatePrinterAsync(PrinterDto printer)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/printers/{printer.Id}", printer);
        await ThrowIfRefusedAsync(response);
        return await response.Content.ReadFromJsonAsync<PrinterDto>();
    }

    /// <inheritdoc cref="CreatePrinterAsync"/>
    public async Task DeletePrinterAsync(int id)
    {
        var response = await _httpClient.DeleteAsync($"api/printers/{id}");
        await ThrowIfRefusedAsync(response);
    }

    /// <summary>
    /// Turn a refusal into the server's own sentence.
    ///
    /// A body is only trusted as a message when it is short and is not JSON: a
    /// <c>ProblemDetails</c> payload or a stack trace rendered on a screen is worse
    /// than a plain statement, so anything that does not look like a sentence falls
    /// back to one written here.
    /// </summary>
    private static async Task ThrowIfRefusedAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = (await response.Content.ReadAsStringAsync()).Trim();

        var usable = body.Length is > 0 and <= 300
                     && !body.StartsWith('{')
                     && !body.StartsWith('<');

        throw new PrinterRegistryException(usable
            ? body
            : $"The registry refused that change · {(int)response.StatusCode} {response.ReasonPhrase}");
    }

    // Reports

    /// <summary>
    /// The Dashboard aggregate for a period. Omit both bounds to get the API's default window,
    /// the current UTC day. Dates are sent round-trip ("o") so the server reads the instant that
    /// was meant rather than a locale-formatted string.
    /// </summary>
    public async Task<DashboardDto?> GetDashboardAsync(DateTime? from = null, DateTime? to = null)
    {
        var query = new List<string>();

        if (from.HasValue)
            query.Add($"from={Uri.EscapeDataString(from.Value.ToString("o"))}");

        if (to.HasValue)
            query.Add($"to={Uri.EscapeDataString(to.Value.ToString("o"))}");

        var url = query.Count == 0
            ? "api/reports/dashboard"
            : $"api/reports/dashboard?{string.Join("&", query)}";

        return await _httpClient.GetFromJsonAsync<DashboardDto>(url);
    }
}

/// <summary>
/// The registry refused a write, and the message is the sentence it refused with.
///
/// It is its own type so a screen can tell "the registry said no, and here is why in
/// words a person can act on" apart from "the network fell over", which is an
/// <see cref="HttpRequestException"/> and needs a different sentence. Catching
/// <see cref="Exception"/> and rendering <c>ex.Message</c> would put a socket error in
/// the place a validation message goes.
/// </summary>
public sealed class PrinterRegistryException : Exception
{
    public PrinterRegistryException(string message) : base(message)
    {
    }
}
