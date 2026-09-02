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