using Restaurant.Blazor.Components;
using Restaurant.UI.Shared.Services;
using Restaurant.Blazor.Services.Printing;
using Restaurant.UI.Shared.Services.Printing;
using Restaurant.UI.Shared.Services.Printing.Network;

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

// Printing from the back office. Two transports, aggregated, and every honest
// distinction between them kept.
//
// THE THING TO UNDERSTAND FIRST: this is Blazor Server, so this C# runs in the
// ASP.NET process and not in the browser. "Bluetooth in the back office" is
// therefore the radio in the machine running this server - a counter PC, a
// mini-PC in the office, or the POS device itself where the back office is served
// from it. It is never the radio in the laptop of a manager who opened the page.
// The screen says which host it speaks for, so that is stated rather than assumed.
//
// Registration order is the order a person sees the transports in, and the network
// goes first because it is the one that works on every host. Bluetooth follows and
// reports honestly when the host has no radio - which is a different fact from
// "no printers found" and is rendered as a different sentence.
builder.Services.AddSingleton<IPrinterTransport, TcpPrinterTransport>();
builder.Services.AddSingleton<IPrinterTransport, WindowsBluetoothPrinterTransport>();

// One selection for the host, held for as long as the process. A back office
// serves one venue from one machine, so a process-wide choice is the right shape;
// persisting it across restarts, and recording which terminal claims which
// printer, is the Devices registry's job and is deliberately not this.
builder.Services.AddSingleton<IPrinterPreference, InMemoryPrinterPreference>();

builder.Services.AddSingleton<IReceiptPrinter>(sp => new TransportReceiptPrinter(
    new CompositePrinterTransport(sp.GetServices<IPrinterTransport>()),
    sp.GetRequiredService<IPrinterPreference>()));

// The preview at /preview/printer is a different thing and keeps its own answer.
// It exists to show the terminal's screen in its honest no-printer state, and now
// that this host has real transports it has to be handed the unavailable one
// explicitly - otherwise opening a preview would start scanning the venue's
// network, and the screen it is previewing would stop being the state it exists
// to show.
builder.Services.AddSingleton<UnavailableReceiptPrinter>();

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