using Microsoft.EntityFrameworkCore;
using Restaurant.Shared.Models;
using System.Reflection.Emit;

namespace Restaurant.Api.Data;

public class RestaurantDbContext : DbContext
{
    public RestaurantDbContext(DbContextOptions<RestaurantDbContext> options)
        : base(options)
    {
    }

    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Table> Tables => Set<Table>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure MenuItem
        modelBuilder.Entity<MenuItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Price).HasColumnType("decimal(18,2)");
            entity.HasIndex(e => e.Category);
        });

        // Configure Table
        modelBuilder.Entity<Table>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TableNumber).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => e.TableNumber).IsUnique();
        });

        // Configure Order
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OrderNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18,2)");
            entity.HasIndex(e => e.OrderNumber).IsUnique();
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);

            entity.HasOne(e => e.Table)
                .WithMany()
                .HasForeignKey(e => e.TableId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.OrderItems)
                .WithOne(oi => oi.Order)
                .HasForeignKey(oi => oi.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure OrderItem
        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Subtotal).HasColumnType("decimal(18,2)");

            entity.HasOne(e => e.MenuItem)
                .WithMany()
                .HasForeignKey(e => e.MenuItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Seed initial data
        SeedData(modelBuilder);
    }

    private void SeedData(ModelBuilder modelBuilder)
    {
        var seedDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Seed Tables
        modelBuilder.Entity<Table>().HasData(
            new Table { Id = 1, TableNumber = "T1", Capacity = 2, IsOccupied = false, Location = "Main Hall" },
            new Table { Id = 2, TableNumber = "T2", Capacity = 4, IsOccupied = false, Location = "Main Hall" },
            new Table { Id = 3, TableNumber = "T3", Capacity = 4, IsOccupied = false, Location = "Patio" },
            new Table { Id = 4, TableNumber = "T4", Capacity = 6, IsOccupied = false, Location = "VIP" }
        );

        // Seed Menu Items with static CreatedAt date
        modelBuilder.Entity<MenuItem>().HasData(
            new MenuItem { Id = 1, Name = "Burger", Description = "Classic beef burger", Price = 12.99m, Category = "Main Course", IsAvailable = true, CreatedAt = seedDate },
            new MenuItem { Id = 2, Name = "Pizza Margherita", Description = "Fresh mozzarella and basil", Price = 14.99m, Category = "Main Course", IsAvailable = true, CreatedAt = seedDate },
            new MenuItem { Id = 3, Name = "Caesar Salad", Description = "Romaine lettuce with Caesar dressing", Price = 8.99m, Category = "Salad", IsAvailable = true, CreatedAt = seedDate },
            new MenuItem { Id = 4, Name = "Pasta Carbonara", Description = "Creamy pasta with bacon", Price = 13.99m, Category = "Main Course", IsAvailable = true, CreatedAt = seedDate },
            new MenuItem { Id = 5, Name = "Coca Cola", Description = "330ml", Price = 2.99m, Category = "Beverage", IsAvailable = true, CreatedAt = seedDate },
            new MenuItem { Id = 6, Name = "Coffee", Description = "Espresso", Price = 3.50m, Category = "Beverage", IsAvailable = true, CreatedAt = seedDate }
        );
    }
}