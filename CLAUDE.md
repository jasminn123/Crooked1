# Crooked (CRKD)

POS, inventory, and stock-forecasting system for a clothing store. A single ASP.NET Core 8 app serves both the JSON API and the static HTML/CSS/JS frontend. Data lives in PostgreSQL (Supabase), accessed with raw SQL through Npgsql. Entity Framework is referenced but not used.

## Layout

```
Dockerfile, .dockerignore, railway.json   # deploy config (build context = repo root)
src/
  CrookedAPI.sln
  CrookedAPI/
    CrookedAPI.csproj
    appsettings.json            # ThresholdForecast options
    Database/DatabaseConfig.cs  # connection string (env DB_CONNECTION_STRING overrides)
    api/
      Program.cs                # startup, static file serving, schema setup (EnsureDatabaseSetup)
      Controllers/              # one controller per domain: Auth, POS, Transaction, Products, Forecasting
      Models/                   # DTOs and records
      Services/                 # background services (ThresholdForecastService)
    Frontend/
      page/                     # one .html per page
      design/                   # one .css per page, plus dashboard.css as the shared admin base
      javascript/               # one .js per page, plus shared layout.js and theme.js
      assets/                   # static images and logo
    wwwroot/images/products/    # uploaded product images (written at runtime)
```

Pages:
- `index` is the login page.
- `PointofSale` is the cashier view.
- The admin pages share a sidebar: `dashboard`, `products`, `inventory`, `transaction-logs`, `sales-history`, `staff-management`, and `forecasting`. Pages marked `data-owner-only` are restricted to the owner role.

## Conventions

- Follow the existing structure. Don't add new top-level folders when an existing layer fits.
- A new page needs three files that share its name: `page/<name>.html`, `design/<name>.css`, and `javascript/<name>.js`, using kebab-case for new names.
- Reuse the shared pieces instead of copying them:
  - `layout.js` provides `apiBase`, dialogs (`showDialog`), toasts, modals with focus trapping, the profile card, logout, the owner-only redirect, and forecast notifications.
  - `theme.js` handles light and dark mode.
  - `dashboard.css` holds the shared admin styles.
  - Admin pages load `theme.js` in `<head>` and `layout.js` before the page's own script.
  - `index.js` currently keeps its own copy of the `showDialog` and `trapFocus` helpers from `layout.js`. Don't add more copies like it.
- Pages link to resources with `../design/`, `../javascript/`, `../assets/`, and `../page/`. Program.cs serves `Frontend/page` and `Frontend` from the site root so those links resolve.
- Call the API with same-origin paths (`/api/...` or `${apiBase}/api/...`). Never hardcode `localhost:5055`, because it breaks the deployed site.
- New backend features go in a controller for their domain under `api/Controllers`, with models in `api/Models`. Schema changes go in `EnsureDatabaseSetup` in Program.cs as idempotent SQL (`IF NOT EXISTS`).

## Run and deploy

- Local: `dotnet run --project src/CrookedAPI`, then open http://localhost:5055.
- Railway builds the root `Dockerfile`. The Dockerfile finds `CrookedAPI.csproj` itself, so the build works whether the service's Root Directory is empty or `src`. The app listens on Railway's `PORT` variable, and Program.cs handles that.
- Production runs on Linux, so file names are case-sensitive. Links must match file names exactly; for example, `PointofSale.html` and `PointofSale.css` but `PointOfSale.js`.
- Uploaded product images in `wwwroot` are lost on every redeploy unless a Railway volume is mounted at `/app/wwwroot/images/products`.
