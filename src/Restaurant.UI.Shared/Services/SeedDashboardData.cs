using Restaurant.Shared.Models.Dtos;

namespace Restaurant.UI.Shared.Services;

/// <summary>
/// A design-time stand-in for one day's Dashboard aggregate, so the KPI strip, the daypart chart
/// and the top-items list render without PostgreSQL.
///
/// <strong>Every figure below is invented.</strong> Nothing here is derived, sampled, projected or
/// averaged from anything — it is a plausible mid-size dinner service written by hand so the chart
/// has four bars of different heights and the top-items list has three rows of different lengths.
/// It is not a cache, not an offline mode, and not a forecast. Anything that renders it must say
/// so out loud (see <see cref="DashboardDataSource.IsLive"/>).
///
/// The one property the numbers do have is internal consistency, because a screen whose tiles
/// contradict each other is harder to design against than one whose tiles agree: the dayparts sum
/// to <see cref="DashboardDto.NetSales"/> and to <see cref="DashboardDto.OrderCount"/>, and
/// <see cref="DashboardDto.AverageTicket"/> is net sales over order count rounded to cents. That
/// last one is why the average ticket reads 22.74 where the handbook's artboard label reads
/// $22.70 — the artboard's four figures were drawn independently and do not reconcile; these do.
///
/// The top items use ids, names and prices from <see cref="SeedMenuData"/>, so a reviewer clicking
/// from the Dashboard to the Menu manager sees the same menu on both screens.
/// </summary>
public static class SeedDashboardData
{
    public static DashboardDto Create()
    {
        // The window is the current UTC day, matching what the API defaults to, so the fallback
        // and the live path describe the same period.
        var from = DateTime.UtcNow.Date;

        return new DashboardDto
        {
            From = from,
            To = from.AddDays(1),
            NetSales = 4820.00m,
            OrderCount = 212,
            AverageTicket = 22.74m,
            Dayparts = new List<DaypartSliceDto>
            {
                new() { Name = "Breakfast",  Sales =  610.00m, OrderCount = 38 },
                new() { Name = "Lunch",      Sales = 1480.00m, OrderCount = 71 },
                new() { Name = "Dinner",     Sales = 2320.00m, OrderCount = 84 },
                new() { Name = "Late night", Sales =  410.00m, OrderCount = 19 }
            },
            TopItems = new List<TopItemDto>
            {
                new() { MenuItemId = 2, Name = "Pizza Margherita", QuantitySold = 99,  Sales = 1484.01m },
                new() { MenuItemId = 1, Name = "Burger",           QuantitySold = 114, Sales = 1480.86m },
                new() { MenuItemId = 3, Name = "Caesar Salad",     QuantitySold = 86,  Sales =  773.14m }
            }
        };
    }
}
