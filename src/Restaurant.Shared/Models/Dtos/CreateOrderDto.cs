namespace Restaurant.Shared.Models.Dtos;

public class CreateOrderDto
{
    public int? TableId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}