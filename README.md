C:\Code\POC-Razor-Hospitality\Restaurant\
├── Restaurant.sln
└── src\
    ├── Restaurant.Shared\           (Domain models, DTOs, interfaces)
    │   ├── Restaurant.Shared.csproj
    │   ├── Class1.cs (can delete)
    │   └── Models\                 
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


Restaurant.sln
Restaurant.Api (ASP.NET Core Web API + SignalR + EF Core DbContext + Migrations)
Restaurant.Mobile (MAUI Blazor Hybrid app) using local EF Core SQLite and remote sync via Web API + SignalR client
Restaurant.Blazor (Blazor Web App (Server/Interactive)) — back office UI calling API
Restaurant.Shared (class library with DTOs/entities and interfaces)
Restaurant.UI.Shared (Razor Class Library for shared UI components)


Key Files
src/Restaurant.Api/Startup/Program.cs - configure services, EF, SignalR
src/Restaurant.Api/Data/RestaurantDbContext.cs - EF Core models & migrations
src/Restaurant.Api/Hubs/OrdersHub.cs - SignalR hub
src/Restaurant.Shared/Models/*.cs - shared entity/DTO definitions
src/Restaurant.UI.Shared - Razor components (order list, order editor)
src/Restaurant.Mobile/MainPage.razor / MauiProgram.cs - BlazorWebView and local DB setup


Progress Summary:
Completed:
•	Step 1: Solution with 5 projects (Shared, API, UI.Shared, Blazor, Mobile)
•	Step 2: Domain models and DTOs
•	Step 3: API backend with database
•	Step 4: Razor Class Library (shared UI components)
•	Step 5: Back-office Blazor Web App (PWA with SignalR)
Remaining:
•	Step 6: Mobile app configuration (MAUI Blazor Hybrid)
•	Step 7: Offline sync service for mobile
•	Step 8: Documentation and deployment notes
