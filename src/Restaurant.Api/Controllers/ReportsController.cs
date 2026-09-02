using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Shared.Models;
using Restaurant.Shared.Models.Dtos;

namespace Restaurant.Api.Controllers
{
    /// <summary>
    /// Read-only aggregations for the back office. Nothing here writes.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class ReportsController : ControllerBase
    {
        private const string Breakfast = "Breakfast";
        private const string Lunch = "Lunch";
        private const string Dinner = "Dinner";
        private const string LateNight = "Late night";

        /// <summary>The four service periods, in the order the Dashboard's chart draws them.
        /// Every response carries all four, zero-filled where nothing sold.</summary>
        private static readonly string[] Dayparts = { Breakfast, Lunch, Dinner, LateNight };

        /// <summary>
        /// How many rows the top-items list returns. The Dashboard draws three, and the cap lives
        /// here rather than on the client so the page renders exactly what it is handed instead of
        /// silently discarding rows. If another consumer ever needs a different depth, this becomes
        /// a query parameter — it should not become an unbounded list.
        /// </summary>
        private const int TopItemCount = 3;

        private readonly RestaurantDbContext _context;

        public ReportsController(RestaurantDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// The Dashboard's figures for one period: net sales, order count, average ticket, the
        /// four-way daypart split and the best-selling items.
        /// </summary>
        /// <param name="from">Start of the window, inclusive. UTC.</param>
        /// <param name="to">End of the window, exclusive. UTC.</param>
        [HttpGet("dashboard")]
        public async Task<ActionResult<DashboardDto>> GetDashboard(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null)
        {
            // The window. A real business day starts when the venue says it does — it is a
            // configured boundary (often 04:00 local, so a late-night service closes on the day it
            // began) and it needs a venue entity to hang off. There is none: GAP-13. So the default
            // below is a stated stand-in, not a decision — UTC midnight to UTC midnight, which is
            // wrong for every venue that is not on UTC and wrong for any venue trading past
            // midnight. It is here so the endpoint has a defined default, and it must be replaced
            // by the venue's configured day when GAP-13 is closed.
            var fromUtc = AsUtc(from);
            var toUtc = AsUtc(to);

            if (fromUtc is null && toUtc is null)
            {
                fromUtc = DateTime.UtcNow.Date;
                toUtc = fromUtc.Value.AddDays(1);
            }
            else if (toUtc is null)
            {
                toUtc = fromUtc!.Value.AddDays(1);
            }
            else if (fromUtc is null)
            {
                fromUtc = toUtc.Value.AddDays(-1);
            }

            if (toUtc <= fromUtc)
                return BadRequest("'to' must be later than 'from'.");

            var start = fromUtc.Value;
            var end = toUtc.Value;

            // One filtered query for the whole screen. Every figure below is a different grouping
            // of the same rows, so grouping in memory costs one pass over a day of orders and saves
            // three further round trips. The projection keeps the payload to the columns actually
            // used rather than materializing whole entities.
            //
            // Cancelled orders are excluded from every figure: a voided order is not sales, so it
            // must not reach net sales, the order count, the average ticket, the daypart split or
            // the top items. Excluding it once, here, is what keeps those five consistent with
            // each other.
            var orders = await _context.Orders
                .Where(o => o.Status != OrderStatus.Cancelled
                            && o.CreatedAt >= start
                            && o.CreatedAt < end)
                .Select(o => new
                {
                    o.CreatedAt,
                    o.TotalAmount,
                    Lines = o.OrderItems.Select(oi => new
                    {
                        oi.MenuItemId,
                        Name = oi.MenuItem != null ? oi.MenuItem.Name : string.Empty,
                        oi.Quantity,
                        oi.Subtotal
                    }).ToList()
                })
                .ToListAsync();

            // Net sales is Order.TotalAmount, which OrdersController sets to the sum of its lines'
            // subtotals. Top-item sales sum those same subtotals, so the two reconcile today. They
            // would stop reconciling the moment tax, discounts, service charge or tips existed —
            // none of which has anywhere to be stored (GAP-08).
            var netSales = orders.Sum(o => o.TotalAmount);
            var orderCount = orders.Count;

            // Money, so round to cents rather than handing the client a repeating decimal to
            // format. Zero orders divides by zero, hence the guard.
            var averageTicket = orderCount == 0
                ? 0m
                : Math.Round(netSales / orderCount, 2, MidpointRounding.AwayFromZero);

            var grouped = orders
                .GroupBy(o => DaypartFor(o.CreatedAt.Hour))
                .ToDictionary(
                    g => g.Key,
                    g => new { Sales = g.Sum(o => o.TotalAmount), Count = g.Count() });

            // Projected from the fixed list rather than from the groups, so all four slices are
            // always present, always in service order, and an empty daypart reads as zero rather
            // than as a missing bar.
            var dayparts = Dayparts
                .Select(name =>
                {
                    grouped.TryGetValue(name, out var slice);
                    return new DaypartSliceDto
                    {
                        Name = name,
                        Sales = slice?.Sales ?? 0m,
                        OrderCount = slice?.Count ?? 0
                    };
                })
                .ToList();

            var topItems = orders
                .SelectMany(o => o.Lines)
                .GroupBy(l => l.MenuItemId)
                .Select(g => new TopItemDto
                {
                    MenuItemId = g.Key,
                    Name = g.Select(l => l.Name).FirstOrDefault() ?? string.Empty,
                    QuantitySold = g.Sum(l => l.Quantity),
                    Sales = g.Sum(l => l.Subtotal)
                })
                // By revenue, which is what the card's right-hand figure ranks on. Name then id
                // break ties so the order is stable across calls rather than left to grouping.
                .OrderByDescending(t => t.Sales)
                .ThenBy(t => t.Name, StringComparer.Ordinal)
                .ThenBy(t => t.MenuItemId)
                .Take(TopItemCount)
                .ToList();

            // A period with no orders returns zeros, four zeroed dayparts and an empty top-items
            // list. Never null, and never a 404 — "nothing sold yet today" is a real answer.
            return Ok(new DashboardDto
            {
                From = start,
                To = end,
                NetSales = netSales,
                OrderCount = orderCount,
                AverageTicket = averageTicket,
                Dayparts = dayparts,
                TopItems = topItems
            });
        }

        /// <summary>
        /// Daypart boundaries by hour: breakfast under 11, lunch 11 to 16, dinner 16 to 22, late
        /// night otherwise. The hour is <c>Order.CreatedAt</c>'s, which is UTC — the same GAP-13
        /// stand-in as the default window, and wrong for the same reason. Real service periods are
        /// per-venue configuration.
        /// </summary>
        private static string DaypartFor(int hour) => hour switch
        {
            < 11 => Breakfast,
            < 16 => Lunch,
            < 22 => Dinner,
            _ => LateNight
        };

        /// <summary>
        /// Query-string dates bind with <see cref="DateTimeKind.Unspecified"/>, and Npgsql refuses
        /// to compare an unspecified-kind value against a <c>timestamp with time zone</c> column.
        /// The parameters are documented as UTC, so an unspecified value is stamped UTC; a value
        /// that arrived with an offset is converted rather than reinterpreted.
        /// </summary>
        private static DateTime? AsUtc(DateTime? value)
        {
            if (value is null)
                return null;

            return value.Value.Kind switch
            {
                DateTimeKind.Utc => value.Value,
                DateTimeKind.Local => value.Value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            };
        }
    }
}
