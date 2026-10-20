/* =========================
   FORECAST TABLE
   Products with no sales in the last 7 days start hidden to keep the
   table short. "Show or hide products" lists every product so people
   can choose which ones appear; choices are kept in this browser.
========================= */
const VELOCITY_TONES = { fast: 'success', moderate: 'info' };
const VISIBILITY_KEY = 'crkd-forecast-visibility';

let forecastProducts = [];
// Product id -> shown (true/false) for products someone showed or hid by hand.
// Products without a choice follow hasSales().
const visibilityChoices = readChoices();

function readChoices() {
    try {
        const saved = JSON.parse(localStorage.getItem(VISIBILITY_KEY) || '{}');
        return new Map(Object.entries(saved).map(([id, shown]) => [Number(id), Boolean(shown)]));
    } catch {
        return new Map();
    }
}

function saveChoices() {
    try {
        localStorage.setItem(VISIBILITY_KEY, JSON.stringify(Object.fromEntries(visibilityChoices)));
    } catch {
        // Without storage the choices still apply until the page is reloaded.
    }
}

const hasSales = product => product.dailySales.some(units => units > 0);
const isShown = product => visibilityChoices.has(product.id) ? visibilityChoices.get(product.id) : hasSales(product);
const productName = product => UI.variantName(product.productName, product.size, product.color);

async function loadForecast({ quiet = false } = {}) {
    const tableBody = document.getElementById('forecastTableBody');
    if (!tableBody) return;
    if (!quiet) UI.tableSkeleton(tableBody, 4, 6);

    try {
        const analytics = await UI.fetchJson(`${apiBase}/api/Forecasting/daily-sales`);
        forecastProducts = analytics.products;
        renderProductOptions();
        renderForecastTable({ fade: !quiet });
    } catch (error) {
        console.error('Forecast Error:', error);
        document.getElementById('forecastSummary').textContent = 'The forecast could not be loaded.';
        UI.tableMessage(tableBody, 4, {
            isError: true,
            title: 'Unable to load the forecast',
            message: error.message,
            actionLabel: 'Try again',
            onAction: () => loadForecast()
        });
    }
}

function renderForecastTable({ fade = false } = {}) {
    const tableBody = document.getElementById('forecastTableBody');
    const summary = document.getElementById('forecastSummary');
    const shown = forecastProducts.filter(isShown);
    const hidden = forecastProducts.length - shown.length;

    summary.textContent = forecastProducts.length
        ? `Showing ${shown.length} of ${forecastProducts.length} product${forecastProducts.length === 1 ? '' : 's'}${hidden ? ` (${hidden} hidden)` : ''}.`
        : '';

    if (!forecastProducts.length) {
        UI.tableMessage(tableBody, 4, {
            icon: 'fa-chart-column',
            title: 'No forecast data yet',
            message: 'Forecasts appear once there are active products.'
        });
        return;
    }

    if (!shown.length) {
        UI.tableMessage(tableBody, 4, {
            icon: 'fa-eye-slash',
            title: 'No products to show',
            message: 'None of the products had sales in the last 7 days, or they are all hidden.',
            actionLabel: 'Show all products',
            actionIcon: 'fa-eye',
            onAction: showAllProducts
        });
        return;
    }

    UI.fillTable(tableBody, shown.map(product => {
        const velocity = String(product.salesVelocityRating || 'slow').toLowerCase();
        const label = velocity.charAt(0).toUpperCase() + velocity.slice(1);

        const row = document.createElement('tr');
        row.append(
            UI.cell(productName(product)),
            UI.cell(Number(product.averageDailySales).toFixed(2)),
            UI.cell(String(product.lowStockThreshold)),
            UI.cell(UI.badge(label, VELOCITY_TONES[velocity]))
        );
        return row;
    }), { fade });
}

// One checkbox per product. Rebuilding keeps keyboard focus on the same product's checkbox.
function renderProductOptions() {
    const container = document.getElementById('forecastProductOptions');
    const focusedId = document.activeElement?.dataset?.productId;

    container.replaceChildren(...forecastProducts.map(product => {
        const option = UI.el('label', 'forecast-product-option');
        const checkbox = UI.el('input');
        checkbox.type = 'checkbox';
        checkbox.checked = isShown(product);
        checkbox.dataset.productId = product.id;
        checkbox.addEventListener('change', () => {
            visibilityChoices.set(product.id, checkbox.checked);
            saveChoices();
            renderForecastTable();
        });
        option.append(checkbox, UI.el('span', 'forecast-product-name', productName(product)));
        if (!hasSales(product)) option.appendChild(UI.el('span', 'forecast-product-tag', 'No sales'));
        return option;
    }));

    if (focusedId) container.querySelector(`[data-product-id="${focusedId}"]`)?.focus();
}

function showAllProducts() {
    forecastProducts.forEach(product => visibilityChoices.set(product.id, true));
    saveChoices();
    renderProductOptions();
    renderForecastTable();
}

function showProductsWithSales() {
    visibilityChoices.clear();
    saveChoices();
    renderProductOptions();
    renderForecastTable();
}

/* =========================
   PAGE LOAD
========================= */
document.addEventListener('DOMContentLoaded', () => {
    const toggle = document.getElementById('forecastListToggle');
    const panel = document.getElementById('forecastProductPanel');
    toggle.addEventListener('click', () => {
        const open = panel.hidden;
        panel.hidden = !open;
        toggle.setAttribute('aria-expanded', String(open));
    });
    document.getElementById('forecastShowAll').addEventListener('click', showAllProducts);
    document.getElementById('forecastShowSold').addEventListener('click', showProductsWithSales);

    loadForecast();
    Live.on(['forecast', 'transactions', 'exchanges', 'products'], () => loadForecast({ quiet: true }));
});
