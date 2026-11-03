/* =========================
   FORECAST TABLE
   Products with no sales in the last 7 days start hidden to keep the
   table short. "Show or hide products" lists every product so people
   can choose which ones appear; choices are kept in this browser.
========================= */
const COLUMNS = 8;
const VELOCITY_TONES = { fast: 'success', moderate: 'info' };
const TRENDS = {
    up: { label: 'Up', icon: 'fa-arrow-up', tone: 'success' },
    down: { label: 'Down', icon: 'fa-arrow-down', tone: 'warning' },
    stable: { label: 'Stable', icon: 'fa-arrow-right' }
};
const VISIBILITY_KEY = 'crkd-forecast-visibility';

let forecastProducts = [];
// How the server forecasts (days predicted, history needed, ...), from the API.
let forecastSettings = null;
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
    if (!quiet) UI.tableSkeleton(tableBody, COLUMNS, 6);

    try {
        const analytics = await UI.fetchJson(`${apiBase}/api/Forecasting/daily-sales`);
        forecastProducts = analytics.products;
        forecastSettings = analytics.settings;
        renderMethodNotes();
        renderProductOptions();
        renderForecastTable({ fade: !quiet });
    } catch (error) {
        console.error('Forecast Error:', error);
        document.getElementById('forecastSummary').textContent = 'The forecast could not be loaded.';
        UI.tableMessage(tableBody, COLUMNS, {
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
        UI.tableMessage(tableBody, COLUMNS, {
            icon: 'fa-chart-column',
            title: 'No forecast data yet',
            message: 'Forecasts appear once there are active products.'
        });
        return;
    }

    if (!shown.length) {
        UI.tableMessage(tableBody, COLUMNS, {
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
        const sold = product.dailySales.reduce((total, units) => total + units, 0);

        const row = document.createElement('tr');
        row.append(
            UI.cell(productName(product)),
            UI.cell(String(sold)),
            predictionCell(product.forecast),
            trendCell(product.forecast),
            stockCell(product),
            restockCell(product.forecast),
            UI.cell(String(product.lowStockThreshold)),
            UI.cell(UI.badge(label, VELOCITY_TONES[velocity]))
        );
        return row;
    }), { fade });
}

/* =========================
   FORECAST CELLS
========================= */
const formatUnits = value => Number(value).toLocaleString('en-PH', { maximumFractionDigits: 1 });
const plural = (count, word) => `${count} ${word}${count === 1 ? '' : 's'}`;

// A value with smaller lines of detail under it.
function detailedCell(value, ...details) {
    const cell = UI.cell(value);
    details.filter(Boolean).forEach(detail => cell.appendChild(UI.el('span', 'forecast-detail', detail)));
    return cell;
}

// The predicted units, which method made them, and how each method did in the backtest.
function predictionCell(forecast) {
    if (!forecast) return detailedCell('—', 'Waiting for the first forecast');

    const cell = detailedCell(formatUnits(forecast.predictedUnits));
    const method = UI.el('span', 'forecast-detail');
    const isModel = forecast.method === 'ml';
    method.append(
        UI.el('span', `forecast-method${isModel ? ' is-ml' : ''}`, isModel ? 'ML model' : '7-day average'),
        ` · ${isModel ? `likely ${formatUnits(forecast.predictedLow)}–${formatUnits(forecast.predictedHigh)}` : averageReason(forecast)}`
    );
    cell.appendChild(method);

    if (forecast.backtestWeeks > 0) {
        cell.appendChild(UI.el('span', 'forecast-detail',
            `Off by ${formatUnits(forecast.modelError)} (model) vs ${formatUnits(forecast.averageError)} (average) a week`));
    }
    return cell;
}

function averageReason(forecast) {
    if (forecast.reason === 'short-history') {
        return `needs ${forecastSettings?.minHistoryDays ?? 28} days of sales, has ${forecast.historyDays}`;
    }
    if (forecast.reason === 'model-less-accurate') {
        return `more accurate over the last ${plural(forecast.backtestWeeks, 'week')}`;
    }
    if (forecast.reason === 'model-failed') return 'the model could not run';
    return '';
}

function trendCell(forecast) {
    const trend = TRENDS[forecast?.trend];
    if (!trend) return detailedCell('—', forecast ? 'Needs 2 weeks of sales' : '');

    const badge = UI.badge(trend.label, trend.tone);
    const icon = UI.el('i', `fa-solid ${trend.icon}`);
    icon.setAttribute('aria-hidden', 'true');
    badge.prepend(icon);
    return UI.cell(badge);
}

function stockCell(product) {
    const days = product.forecast?.daysOfStockLeft;
    const detail = !product.forecast ? ''
        : product.stockQuantity <= 0 ? 'Out of stock'
        : days === null ? 'No sales expected'
        : `About ${plural(days, 'day')} left`;
    return detailedCell(String(product.stockQuantity), detail);
}

function restockCell(forecast) {
    if (!forecast) return UI.cell('—');
    if (forecast.suggestedRestock <= 0) return detailedCell('None', 'Stock covers the forecast');
    return detailedCell(String(forecast.suggestedRestock), `For ${forecastSettings?.restockDays ?? 14} days of sales`);
}

// Explains the settings in use and how many products the model is forecasting.
function renderMethodNotes() {
    const settings = forecastSettings;
    if (settings) {
        document.getElementById('forecastSettingsNote').textContent =
            `It learns from up to ${settings.historyDays} days of sales and predicts the next ${settings.horizonDays}. `
            + 'Sizes and colors are forecast together and split by their share of recent sales. '
            + `Products with under ${settings.minHistoryDays} days of sales use the 7-day average. `
            + `The low-stock threshold covers ${plural(settings.coverageDays, 'day')} of predicted sales, `
            + `and the restock suggestion covers ${settings.restockDays} days.`;
    }

    const forecasts = forecastProducts.map(product => product.forecast);
    const byModel = forecasts.filter(forecast => forecast?.method === 'ml').length;
    const tooNew = forecasts.filter(forecast => forecast?.reason === 'short-history').length;
    const averageBetter = forecasts.filter(forecast => forecast?.reason === 'model-less-accurate').length;
    const waiting = forecasts.filter(forecast => !forecast).length;
    const parts = [`The ML model is forecasting ${byModel} of ${plural(forecastProducts.length, 'product')}.`];
    if (averageBetter) parts.push(`For ${averageBetter}, the 7-day average tested more accurate.`);
    if (tooNew) parts.push(`${tooNew} ${tooNew === 1 ? 'needs' : 'need'} more sales history.`);
    if (waiting) parts.push(`${waiting} ${waiting === 1 ? 'is' : 'are'} waiting for a first forecast.`);
    document.getElementById('forecastModelStatus').textContent = forecastProducts.length ? parts.join(' ') : '';
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
