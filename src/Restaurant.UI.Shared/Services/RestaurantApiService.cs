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

    public async Task<MenuItemDto?> GetMenuItemAsync(int id)
    {
        return await _httpClient.GetFromJsonAsync<MenuItemDto>($"api/menu/{id}");
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
}