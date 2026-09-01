using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Hubs;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Add OpenAPI/Swagger
builder.Services.AddOpenApi();

// Add DbContext with PostgreSQL
builder.Services.AddDbContext<RestaurantDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsqlOptions => npgsqlOptions.MigrationsAssembly("Restaurant.Api")
    )
);

// Add SignalR
builder.Services.AddSignalR();

// Add CORS for Blazor and Mobile apps
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorAndMobile", policy =>
    {
        policy.WithOrigins(
                "https://localhost:7001",      // Blazor HTTPS
                "http://localhost:5001",       // Blazor HTTP
                "http://10.0.2.2:5000",         // Android emulator
                 "http://192.168.1.89:5001",    // Blazor on your IP
                "http://192.168.1.89:5000"    // API on your IP
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(); 
}

app.UseHttpsRedirection();

// Use CORS
app.UseCors("AllowBlazorAndMobile");

app.UseAuthorization();

app.MapControllers();

// Map SignalR hub
app.MapHub<OrdersHub>("/hubs/orders");

app.Run();