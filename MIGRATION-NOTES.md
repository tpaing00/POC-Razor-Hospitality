# Migration notes — UPOS Fusion UI pass

Branch `feat/upos-fusion-ui`, six commits off `upstream/development` at `d16ac2c`. Nothing is pushed.

This is a design pass, not a feature you asked for. It applies the UPOS Fusion Fluid design system to the
back office (`Restaurant.Blazor`) and builds the Menu manager as a working screen against your real
`MenuItem` data. The specification is the handbook at
`docs/design/UPOS-DESIGN-HANDBOOK.md` **in a separate repository** — Part I is the design language,
Part II-B is the Menu manager screen spec, Part III is the component contract. That repository also holds
the token kit this branch copied from and `docs/GAPS.md`, which every `GAP-NN` citation in the Menu manager
points at. **Ask for it alongside this branch**: without it the citations on screen and the spec pointers
here go nowhere. Treat it as the source — the CSS in this repo is copied verbatim, so fix a value there and
re-copy rather than editing the copy in place.

## What replaced Bootstrap

Bootstrap is deleted — `wwwroot/bootstrap/` is gone and `service-worker.js` no longer caches it. In its
place, two stylesheets copied from the handbook kit into the Razor Class Library and served from
`_content/Restaurant.UI.Shared/css/`: `upos-tokens.css` (CSS custom properties, `--upos-*`) and
`upos-components.css` (class recipes, `.u-*`). There is no CSS framework and no JS framework. Theming is
`data-theme="dark"` and `data-accent="slate|teal|indigo|sky"` on `<html>`; a component that writes a hex
breaks that contract.

`Restaurant.UI.Shared` gained nine components under `Components/Upos/` — `UposButton`, `UposIconButton`,
`StatusChip`, `AllergenChip`, `Pill`, `SegmentedControl`, `StatCard`, `DataRow`, `UposSwitch` — plus
restyled `MenuItemCard` and `OrderStatusBadge`. `OrderStatusBadge` kept its `Status` parameter, so
`Orders.razor` is untouched and still compiles; `MenuItemCard`'s parameters changed. `/kit` is a
development route rendering every component in every state. `Restaurant.Blazor` has a new shell
(`MainLayout`, `NavMenu`), an honest landing page, and `MenuManager.razor` at `/menu`, which replaces the
old `Menu.razor`.

## Things you should know that predate or outlive this work

**Nothing in this app was ever interactive.** `App.razor` declared `@rendermode InteractiveServer` but the
page never loaded `_framework/blazor.web.js`, so no circuit ever started — `Orders.razor`'s SignalR page
included. This is a pre-existing defect, not something the design pass introduced. The script is now on the
page. If you have been wondering why a button never did anything, that is why.

**`MenuController.GetMenuItems()` filtered `.Where(m => m.IsAvailable)`.** An 86'd item disappeared from
the list endpoint, so the back office could never see it or put it back. The endpoint now takes
`[FromQuery] bool includeUnavailable = false` and applies the filter only when it is false. The default
preserves the old behaviour exactly, so the terminal and every existing caller are unaffected; only the
back office asks for the whole menu.

**`appsettings.Development.json` is not in this branch.** `.gitignore` line 34 ignores it, deliberately and
from your initial commit. The file exists on the machine this was built on but was not committed and no
`git add -f` was used. Recreate it at `src/Restaurant.Blazor/appsettings.Development.json` to point the
back office at a local API:

```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "ApiBaseUrl": "http://localhost:5000"
}
```

Decide whether to commit it — it holds no secrets — or to have each developer create their own.

**Three API addresses disagree and all three were left alone.** `appsettings.json` says
`https://192.168.1.89:7000`, the `Program.cs` fallback says `http://192.168.1.89:5000`, and the dev
override above says `http://localhost:5000`. Configuration wins, so the `Program.cs` fallback is dead in
practice. That is your environment, not a UI concern, so nothing here changed it.

**`MenuDataSource`'s seed fallback is a design-time affordance.** It is not a cache and not an offline
mode. When the API cannot be reached at all, the six seeded menu items render so the screen can be
designed without PostgreSQL. `IsLive` says whether the rows currently on screen came from the API, and the
Menu manager prints a line when it is false. An item created while the API is down lives only in memory
and vanishes on the next successful read. An HTTP status error from a reachable API propagates rather than
falling back, so a 500 is never masked by invented rows.

**A 5-second `HttpClient` timeout was added** in `Restaurant.Blazor/Program.cs`. It applies to every
back-office call, not just the menu. Any genuinely long-running call added later needs its own client.

**`Restaurant.Mobile` was not touched.** It inherits the component library and the token kit through
`Restaurant.UI.Shared`, so its shared components will render in the new language the next time it builds.
Nothing was done to check how that looks on a phone.

## Prerendering is off application-wide

`App.razor`'s `<Routes>` now renders with `prerender: false`. The Menu manager needed it — with the API
unreachable, prerendering ran the load twice in two DI scopes and doubled the wait before first paint — but
because the shell makes every route one interactive subtree, the setting could not be scoped to that page
alone. Every route now waits on the circuit for its first paint instead of serving static HTML first. If you
want prerendering back for the rest of the app, the shell has to be restructured so routes are separate
interactive subtrees.

## Deliberately not done

The Dashboard's KPI figures and daypart chart need aggregation endpoints that do not exist, so the landing
page says so instead of showing invented numbers. Integrations, Reports, Employees, Devices and Settings
are drawn in the sidebar as disabled destinations, not wired. The terminal, the KDS and the kiosk are
outside this pass. Inside the Menu manager, the recipe and COGS block, prep steps, allergen toggles,
modifier groups and the minor-category filter chips are rendered in their designed positions as marked
blocked panels citing their `GAP-NN`, because `MenuItem` carries no data for them. None of it is faked.
No schema or migration was changed.

## Build and run

```bash
dotnet build Restaurant.sln
```

`Restaurant.Mobile` fails with `NETSDK1147` unless the `maui-android` workload is installed; the other four
projects build clean. To run the back office, start the API first, then the Blazor app:

```bash
dotnet run --project src/Restaurant.Api        # http://localhost:5000
dotnet run --project src/Restaurant.Blazor     # http://localhost:5001
```

Without the API the Menu manager still renders, on seed data, and says so on the page. Stop any running
instance before rebuilding — a live `Restaurant.Blazor.exe` locks its output directory.
