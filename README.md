C:\Code\POC-Razor-Hospitality\Restaurant\
├── Restaurant.sln
└── src\
    ├── Restaurant.Shared\           (Domain models, DTOs, interfaces)
    │   ├── Restaurant.Shared.csproj
    │   ├── Class1.cs (can delete)
    │   └── Models\                  (we're creating now)
    │       ├── OrderStatus.cs
    │       └── Dtos\
    │
    ├── Restaurant.Api\              (Backend Web API)
    │   ├── Restaurant.Api.csproj
    │   ├── Program.cs
    │   ├── appsettings.json
    │   └── Controllers\
    │
    ├── Restaurant.UI.Shared\        (Razor components)
    │   ├── Restaurant.UI.Shared.csproj
    │   ├── Component1.razor
    │   └── wwwroot\
    │
    ├── Restaurant.Blazor\           (Back-office PWA)
    │   ├── Restaurant.Blazor.csproj
    │   ├── Program.cs
    │   └── Components\
    │
    └── Restaurant.Mobile\           (Android app)
        ├── Restaurant.Mobile.csproj
        ├── MauiProgram.cs
        ├── App.xaml
        └── Resources\
