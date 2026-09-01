using Microsoft.Extensions.Logging;
using Restaurant.UI.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace Restaurant.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        //builder.Logging.AddDebug();
#endif

        // API Base URL - your PC's IP address
        var apiBaseUrl = "http://10.0.2.2:5000/";

        // Add HTTP client for API
        builder.Services.AddScoped(sp =>
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            return new HttpClient(handler) { BaseAddress = new Uri(apiBaseUrl) };
        });

        // Add API service
        builder.Services.AddScoped<RestaurantApiService>();

        // Add local SQLite database (we'll create this next)
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "restaurant.db");
        // builder.Services.AddDbContext<LocalDbContext>(options =>
        //     options.UseSqlite($"Data Source={dbPath}"));

        return builder.Build();
    }
}