# Crooked (CRKD)

POS, inventory, and stock-forecasting system for a clothing store. A single ASP.NET Core 8 app serves both the JSON API and the static HTML/CSS/JS frontend. Data lives in PostgreSQL (Supabase), accessed with raw SQL through Npgsql. Entity Framework is referenced but not used. ClosedXML reads and writes the Excel (.xlsx) files.

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
      Controllers/              # one controller per domain: Auth, POS, Transaction, Exchange, Products, Forecasting
      Models/                   # request DTOs, read models (TransactionViews), sheet rows, value lists
      Services/                 # ThresholdForecastService, TransactionStore (shared reads),
                                # TransactionExcel (xlsx export/import), ShopClock (UTC <-> PH time)
    Frontend/
      page/                     # one .html per page
      design/                   # one .css per page, plus dashboard.css (admin base) and components.css (shared components)
      javascript/               # one .js per page, plus shared ui.js, layout.js and theme.js
      assets/                   # static images and logo
    wwwroot/images/products/    # uploaded product images (written at runtime)
```

Pages:
- `index` is the login page.
- `PointofSale` is the cashier view.
- The admin pages share a sidebar: `dashboard`, `products`, `inventory`, `transaction-logs`, `exchange`, `sales-history`, `staff-management`, and `forecasting`. Pages marked `data-owner-only` are restricted to the owner role.

## Conventions

- Follow the existing structure. Don't add new top-level folders when an existing layer fits.
- A new page needs three files that share its name: `page/<name>.html`, `design/<name>.css`, and `javascript/<name>.js`, using kebab-case for new names. Add its link to the sidebar on every admin page.
- Reuse the shared pieces instead of copying them:
  - `ui.js` (the `UI` namespace) with `components.css`: skeleton loaders (`tableSkeleton`, `cardSkeletons`, `textSkeleton`), table empty/error states (`tableMessage`), `fillTable`, badges (`statusBadge`), `pagination`, toasts, `setLoading` for buttons, `fetchJson`, `download`, `detailPair`, and formatters (`formatPeso`, `variantLabel`).
  - `layout.js` provides `apiBase`, dialogs (`showDialog`), `showToast`, modals with focus trapping, the profile card, logout, the owner-only redirect, and forecast notifications.
  - `theme.js` handles light and dark mode.
  - `dashboard.css` holds the shared admin styles and colour tokens.
  - Admin pages load `theme.js` in `<head>`, then `ui.js`, `layout.js` and the page's own script at the end of `<body>`. The POS loads `ui.js` too.
  - Show a skeleton while data loads and a `tableMessage` (with a retry action) when a request fails, instead of leaving a table blank.
  - Build rows with DOM methods and `textContent` (`UI.cell`, `UI.el`), not `innerHTML` with server data.
  - `index.js` and `PointOfSale.js` still keep their own copies of the `showDialog` and `trapFocus` helpers. Don't add more copies like them.
- Pages link to resources with `../design/`, `../javascript/`, `../assets/`, and `../page/`. Program.cs serves `Frontend/page` and `Frontend` from the site root so those links resolve.
- Call the API with same-origin paths (`/api/...` or `${apiBase}/api/...`). Never hardcode `localhost:5055`, because it breaks the deployed site.
- Bump the `?v=` number on a script or stylesheet link when you change that file, so browsers don't keep the old copy.
- New backend features go in a controller for their domain under `api/Controllers`, with models in `api/Models`. Schema changes go in `EnsureDatabaseSetup` in Program.cs as idempotent SQL (`IF NOT EXISTS`).
- Times are stored in UTC. Convert them with `ShopClock` (Philippine time, UTC+8) instead of `ToLocalTime()`, because the server runs on UTC.

## Transactions and exchanges

- Sale records are read-only in the Transaction Logs. The page lists 10 per page from `GET /api/POS/Transaction/search` (search, status, payment and date filters). Export and import use `/export`, `/import-template` and `/import` with `.xlsx` files only. Import skips Reference IDs that already exist and never changes stock.
- `transactions.payment_method` is `Cash`, `GCash` or `Unknown`.
- An exchanged sale keeps `status = 'Completed'` (so it still counts as revenue) and gets `has_exchange = TRUE`. The API shows it as "Exchanged".
- Exchanges go through `ExchangeController`. They must be for the same product (name and category) at the same price in a different size and/or color. Each exchange:
  - puts the returned variant back into stock and takes the replacement out of stock;
  - moves the sale's `product_daily_sales` count to the new variant;
  - updates `transaction_items` to match what the customer now has;
  - records the change in `exchanges` and `exchange_items`, and writes an `activity_logs` entry.

## Run and deploy

- Local: `dotnet run --project src/CrookedAPI`, then open http://localhost:5055.
- Railway builds the root `Dockerfile`. The Dockerfile finds `CrookedAPI.csproj` itself, so the build works whether the service's Root Directory is empty or `src`. The app listens on Railway's `PORT` variable, and Program.cs handles that.
- Production runs on Linux, so file names are case-sensitive. Links must match file names exactly; for example, `PointofSale.html` and `PointofSale.css` but `PointOfSale.js`.
- Uploaded product images in `wwwroot` are lost on every redeploy unless a Railway volume is mounted at `/app/wwwroot/images/products`.
