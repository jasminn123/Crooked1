/* =========================
   FORECAST TABLE
========================= */
const VELOCITY_TONES = { fast: 'success', moderate: 'info' };

async function loadForecast() {
    const tableBody = document.getElementById('forecastTableBody');
    if (!tableBody) return;
    UI.tableSkeleton(tableBody, 4, 6);

    try {
        const analytics = await UI.fetchJson(`${apiBase}/api/Forecasting/daily-sales`);
        renderForecastTable(tableBody, analytics.products);
    } catch (error) {
        console.error('Forecast Error:', error);
        UI.tableMessage(tableBody, 4, {
            isError: true,
            title: 'Unable to load the forecast',
            message: error.message,
            actionLabel: 'Try again',
            onAction: loadForecast
        });
    }
}

function renderForecastTable(tableBody, products) {
    if (!products.length) {
        UI.tableMessage(tableBody, 4, {
            icon: 'fa-chart-column',
            title: 'No forecast data yet',
            message: 'Forecasts appear once active products have sales.'
        });
        return;
    }

    UI.fillTable(tableBody, products.map(product => {
        const velocity = String(product.salesVelocityRating || 'slow').toLowerCase();
        const label = velocity.charAt(0).toUpperCase() + velocity.slice(1);

        const row = document.createElement('tr');
        row.append(
            UI.cell(product.productName),
            UI.cell(Number(product.averageDailySales).toFixed(2)),
            UI.cell(String(product.lowStockThreshold)),
            UI.cell(UI.badge(label, VELOCITY_TONES[velocity]))
        );
        return row;
    }));
}

/* =========================
   PAGE LOAD
========================= */
document.addEventListener('DOMContentLoaded', loadForecast);
