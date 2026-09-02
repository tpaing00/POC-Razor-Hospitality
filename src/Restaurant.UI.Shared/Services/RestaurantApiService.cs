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