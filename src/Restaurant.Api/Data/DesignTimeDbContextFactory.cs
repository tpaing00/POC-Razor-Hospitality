using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Restaurant.Api.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RestaurantDbContext>
{
    public RestaurantDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<RestaurantDbContext>();

        // Use the connection string from appsettings.json
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=RestaurantDb;Username=postgres;Password=123*777");

        return new RestaurantDbContext(optionsBuilder.Options);
    }
}