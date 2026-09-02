namespace Restaurant.Shared.Models.Dtos;

/// <summary>
/// One period's aggregate for the back-office Dashboard, as returned by
/// <c>GET /api/reports/dashboard</c>.
///
/// It carries only figures the model can actually produce. The Dashboard design also asks for
/// covers, labor percent and comps flagged for review; none of those has a source — there is no
/// guest count on <c>Order</c> (GAP-07), no employee, shift or wage record (GAP-09), and no comp
/// record, since <c>OrderStatus.Cancelled</c> voids a whole order rather than individual lines
/// (GAP-08). They are deliberately absent from this DTO rather than present and always null: a
/// null field reads as "no data for this period", which would be a lie about a metric that has
/// no source at all. The page renders those tiles as blocked, citing the gap.
///
/// Nothing here is ever null. A period with no orders returns zeroed figures, four zeroed
/// dayparts and an empty <see cref="TopItems"/>, so the client needs no null handling.
/// </summary>
public class DashboardDto
{
    /// <summary>Start of the aggregated window, inclusive, in UTC. Echoed back so the page can
    /// state the period it is showing rather than assume it.</summary>
    public DateTime From { get; set; }

    /// <summary>End of the aggregated window, exclusive, in UTC.</summary>
    public DateTime To { get; set; }

    /// <summary>Sum of <c>Order.TotalAmount</c> over the window, cancelled orders excluded.</summary>
    public decimal NetSales { get; set; }

    /// <summary>Number of orders in the window, cancelled orders excluded.</summary>
    public int OrderCount { get; set; }

    /// <summary><see cref="NetSales"/> divided by <see cref="OrderCount"/>, rounded to cents;
    /// zero when there are no orders.</summary>
    public decimal AverageTicket { get; set; }

    /// <summary>
    /// Always the same four slices, in service order: Breakfast, Lunch, Dinner, Late night. A
    /// daypart with no sales is present with zeros rather than omitted, so the chart keeps a
    /// fixed axis and "sold nothing" stays distinguishable from "not reported".
    /// </summary>
    public List<DaypartSliceDto> Dayparts { get; set; } = new();

    /// <summary>Best-selling items by sales value, highest first. Empty when nothing sold.</summary>
    public List<TopItemDto> TopItems { get; set; } = new();
}

/// <summary>One service period's share of the window.</summary>
public class DaypartSliceDto
{
    /// <summary>Breakfast, Lunch, Dinner or Late night.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Sum of <c>Order.TotalAmount</c> for orders opened in this period.</summary>
    public decimal Sales { get; set; }

    /// <summary>Number of orders opened in this period.</summary>
    public int OrderCount { get; set; }
}

/// <summary>One menu item's contribution over the window, summed across every order line.</summary>
public class TopItemDto
{
    public int MenuItemId { get; set; }

    /// <summary>The item's name at read time, from <c>MenuItem</c>. Empty if the line's menu
    /// item no longer resolves.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Units sold, summed from <c>OrderItem.Quantity</c>.</summary>
    public int QuantitySold { get; set; }

    /// <summary>Revenue, summed from <c>OrderItem.Subtotal</c>.</summary>
    public decimal Sales { get; set; }
}
