using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Hubs;
using Restaurant.Shared.Models;
using Restaurant.Shared.Models.Dtos;

namespace Restaurant.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrdersController : ControllerBase
    {
        private readonly RestaurantDbContext _context;
        private readonly IHubContext<OrdersHub> _hubContext;

        public OrdersController(RestaurantDbContext context, IHubContext<OrdersHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<OrderDto>>> GetOrders()
        {
            var orders = await _context.Orders
                .Include(o => o.Table)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.MenuItem)
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => MapToDto(o))
                .ToListAsync();

            return Ok(orders);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<OrderDto>> GetOrder(int id)
        {
            var order = await _context.Orders
                .Include(o => o.Table)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.MenuItem)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
                return NotFound();

            return Ok(MapToDto(order));
        }

        [HttpPost]
        public async Task<ActionResult<OrderDto>> CreateOrder(CreateOrderDto dto)
        {
            // Generate order number
            var orderNumber = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}";

            var order = new Order
            {
                OrderNumber = orderNumber,
                TableId = dto.TableId,
                CustomerName = dto.CustomerName,
                Notes = dto.Notes,
                Status = OrderStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            // Add order items
            foreach (var itemDto in dto.Items)
            {
                var menuItem = await _context.MenuItems.FindAsync(itemDto.MenuItemId);
                if (menuItem == null)
                    return BadRequest($"Menu item {itemDto.MenuItemId} not found");

                var orderItem = new OrderItem
                {
                    MenuItemId = itemDto.MenuItemId,
                    Quantity = itemDto.Quantity,
                    UnitPrice = menuItem.Price,
                    Subtotal = menuItem.Price * itemDto.Quantity,
                    SpecialInstructions = itemDto.SpecialInstructions
                };

                order.OrderItems.Add(orderItem);
            }

            // Calculate total
            order.TotalAmount = order.OrderItems.Sum(oi => oi.Subtotal);

            // Mark table as occupied
            if (order.TableId.HasValue)
            {
                var table = await _context.Tables.FindAsync(order.TableId.Value);
                if (table != null)
                    table.IsOccupied = true;
            }

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Reload with navigation properties
            await _context.Entry(order)
                .Collection(o => o.OrderItems)
                .Query()
                .Include(oi => oi.MenuItem)
                .LoadAsync();

            if (order.TableId.HasValue)
            {
                await _context.Entry(order)
                    .Reference(o => o.Table)
                    .LoadAsync();
            }

            // Notify via SignalR
            await _hubContext.Clients.All.SendAsync("ReceiveNewOrder", order.Id, order.OrderNumber);

            var result = MapToDto(order);
            return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, result);
        }

        [HttpPatch("{id}/status")]
        public async Task<IActionResult> UpdateOrderStatus(int id, UpdateOrderStatusDto dto)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order == null)
                return NotFound();

            order.Status = dto.Status;
            order.UpdatedAt = DateTime.UtcNow;

            if (dto.Status == OrderStatus.Completed)
            {
                order.CompletedAt = DateTime.UtcNow;

                // Free up table
                if (order.TableId.HasValue)
                {
                    var table = await _context.Tables.FindAsync(order.TableId.Value);
                    if (table != null)
                        table.IsOccupied = false;
                }
            }

            await _context.SaveChangesAsync();

            // Notify via SignalR
            await _hubContext.Clients.All.SendAsync("ReceiveOrderStatusUpdate", order.Id, order.Status.ToString());

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteOrder(int id)
        {
            var order = await _context.Orders
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
                return NotFound();

            // Free up table
            if (order.TableId.HasValue)
            {
                var table = await _context.Tables.FindAsync(order.TableId.Value);
                if (table != null)
                    table.IsOccupied = false;
            }

            _context.Orders.Remove(order);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private static OrderDto MapToDto(Order order)
        {
            return new OrderDto
            {
                Id = order.Id,
                OrderNumber = order.OrderNumber,
                TableId = order.TableId,
                TableNumber = order.Table?.TableNumber,
                CustomerName = order.CustomerName,
                Status = order.Status,
                TotalAmount = order.TotalAmount,
                CreatedAt = order.CreatedAt,
                UpdatedAt = order.UpdatedAt,
                Notes = order.Notes,
                Items = order.OrderItems.Select(oi => new OrderItemDto
                {
                    MenuItemId = oi.MenuItemId,
                    MenuItemName = oi.MenuItem?.Name ?? "",
                    Quantity = oi.Quantity,
                    UnitPrice = oi.UnitPrice,
                    Subtotal = oi.Subtotal,
                    SpecialInstructions = oi.SpecialInstructions
                }).ToList()
            };
        }
    }
}
