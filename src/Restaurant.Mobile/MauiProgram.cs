using Microsoft.Extensions.Logging;
using Restaurant.Mobile.Services;
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
            return new HttpClient(handler)
            {
                BaseAddress = new Uri(apiBaseUrl),
                // The terminal has to keep working with no network (handbook §11), so an
                // unreachable API must fail fast enough for MenuDataSource to fall back
                // before the first paint. The default 100s would hold the Order entry
                // grid empty for a minute and a half; 5s is well above a cold local API
                // and matches the back office.
                Timeout = TimeSpan.FromSeconds(5)
            };
        });

        // Add API service
        builder.Services.AddScoped<RestaurantApiService>();

        // The Order entry screen's data dependency, and the reason the terminal can be
        // opened at a bench with no API: MenuDataSource reads through
        // RestaurantApiService, falls back to seed data when the API cannot be reached
        // at all, and reports which of the two is on screen through IsLive. The back
        // office already registers it (Restaurant.Blazor/Program.cs); the shared screen
        // needs it in both hosts.
        builder.Services.AddScoped<MenuDataSource>();

        // The terminal hides Android's status bar, so the shell's top bar has to
        // carry the battery and the connection itself (handbook §12, Part II-A ·
        // Handheld). Restaurant.UI.Shared owns the question as IDeviceStatus and
        // cannot answer it — it has no MAUI reference — so this host registers the
        // implementation that reads Battery.Default and Connectivity.Default.
        //
        // Singleton, not scoped: it holds two platform event subscriptions and
        // there is exactly one device behind them. It is disposed with the
        // container.
        builder.Services.AddSingleton<IDeviceStatus, MauiDeviceStatus>();

        // Add local SQLite database (we'll create this next)
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "restaurant.db");
        // builder.Services.AddDbContext<LocalDbContext>(options =>
        //     options.UseSqlite($"Data Source={dbPath}"));

        return builder.Build();
    }
}