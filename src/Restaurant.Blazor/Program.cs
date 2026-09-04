using Restaurant.Blazor.Components;
using Restaurant.UI.Shared.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    // HTTP
    options.ListenAnyIP(5001);

    // HTTPS
    options.ListenAnyIP(5002);
    //options.ListenAnyIP(5002, listenOptions =>
    //{
    //    listenOptions.UseHttps(
    //        @"C:\Code\Restaurant-Blazor-Dev.pfx",
    //        "RestaurantDev123!");
    //});
});

// Add services to the container
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure API base address
var apiBaseUrl = builder.Configuration.GetValue<string>("ApiBaseUrl") ?? "http://192.168.1.89:5000";
//192.168-17-35 
//192.168-1-89


// Add HTTP client for API
//builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });
// Add HTTP client for API with explicit handler
builder.Services.AddScoped(sp =>
{
    var handler = new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    };
    var httpClient = new HttpClient(handler)
    {
        BaseAddress = new Uri(apiBaseUrl),
        // Prerender runs these calls inline, so the default 100s timeout turns an unreachable
        // API into a hung page. 5s is well above a cold-start local API and well below the
        // ~21s Windows TCP connect timeout, so MenuDataSource falls back promptly instead.
        Timeout = TimeSpan.FromSeconds(5)
    };
    return httpClient;
});
Console.WriteLine($"API Base URL NEW: {apiBaseUrl}");
// Add API service
builder.Services.AddScoped<RestaurantApiService>();
builder.Services.AddScoped<MenuDataSource>();
builder.Services.AddScoped<DashboardDataSource>();

// The terminal preview renders the same TerminalShell Restaurant.Mobile does, and
// that shell now reads the device's battery and connection (handbook §12). This
// host is a desktop browser with no device to read, so it registers the answer that
// says so. Nothing here invents a charge level: the shell draws the absence of a
// reading in the blocked treatment, which is what keeps the preview honest about
// being a preview.
builder.Services.AddSingleton<IDeviceStatus, UnknownDeviceStatus>();

// The same shell now follows the OS theme on the device (Part II-A · Order entry ·
// Dark). This host has no OS theme worth reading: it is a browser, and the theme
// on this page is already the rail toggle's to set. Registering the answer that
// says so is what keeps the two from fighting — the preview renders the terminal
// in whichever theme the person working on it has chosen, and nothing stamps
// data-theme over their choice.
builder.Services.AddSingleton<ISystemTheme, UnknownSystemTheme>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();