using Microsoft.AspNetCore.SignalR;

namespace Restaurant.Api.Hubs;

public class OrdersHub : Hub
{
    // Called when a new order is created
    public async Task NotifyNewOrder(int orderId, string orderNumber)
    {
        await Clients.All.SendAsync("ReceiveNewOrder", orderId, orderNumber);
    }

    // Called when order status changes
    public async Task NotifyOrderStatusChanged(int orderId, string status)
    {
        await Clients.All.SendAsync("ReceiveOrderStatusUpdate", orderId, status);
    }

    // Called when order is completed
    public async Task NotifyOrderCompleted(int orderId)
    {
        await Clients.All.SendAsync("ReceiveOrderCompleted", orderId);
    }

    // Client can join a specific order "room" to receive updates only for that order
    public async Task JoinOrderGroup(int orderId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"Order_{orderId}");
    }

    public async Task LeaveOrderGroup(int orderId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Order_{orderId}");
    }
}