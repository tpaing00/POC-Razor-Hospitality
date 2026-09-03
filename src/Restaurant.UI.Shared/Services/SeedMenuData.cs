using Restaurant.Shared.Models.Dtos;

namespace Restaurant.UI.Shared.Services;

/// <summary>
/// A design-time stand-in for the menu, mirroring the six items seeded by
/// <c>RestaurantDbContext.SeedData</c> so the back office renders without PostgreSQL.
///
/// This is not a cache, not an offline mode, and not a substitute for the API. It exists
/// only so design work can proceed against a screen full of plausible rows; anything that
/// reads it must say so out loud (see <see cref="MenuDataSource.IsLive"/>).
///
/// One item — Pasta Carbonara — is flipped unavailable, which the database seed does not do,
/// so the 86'd treatment is visible without a database to 86 anything in.
/// </summary>
public static class SeedMenuData
{
    public static List<MenuItemDto> Create() => new()
    {
        new MenuItemDto { Id = 1, Name = "Burger", Description = "Classic beef burger", Price = 12.99m, Category = "Main Course", IsAvailable = true },
        new MenuItemDto { Id = 2, Name = "Pizza Margherita", Description = "Fresh mozzarella and basil", Price = 14.99m, Category = "Main Course", IsAvailable = true },
        new MenuItemDto { Id = 3, Name = "Caesar Salad", Description = "Romaine lettuce with Caesar dressing", Price = 8.99m, Category = "Salad", IsAvailable = true },
        new MenuItemDto { Id = 4, Name = "Pasta Carbonara", Description = "Creamy pasta with bacon", Price = 13.99m, Category = "Main Course", IsAvailable = false },
        new MenuItemDto { Id = 5, Name = "Coca Cola", Description = "330ml", Price = 2.99m, Category = "Beverage", IsAvailable = true },
        new MenuItemDto { Id = 6, Name = "Coffee", Description = "Espresso", Price = 3.50m, Category = "Beverage", IsAvailable = true }
    };
}
