/* =========================
   FORECAST TABLE
========================= */
async function loadForecast() {
    try {
        const response = await fetch(`${apiBase}/api/Forecasting/daily-sales`);
        if (!response.ok) throw new Error(`Daily sales request failed (${response.status})`);

        const analytics = await response.json();
        renderForecastTable(analytics.products);
    } catch (error) {
        console.error('Forecast Error:', error);
    }
}

function renderForecastTable(products) {
    const tableBody = document.getElementById('forecastTableBody');
    if (!tableBody) return;

    tableBody.replaceChildren();
    products.forEach(product => {
        const row = document.createElement('tr');
        [
            product.productName,
            Number(product.averageDailySales).toFixed(2),
            product.lowStockThreshold,
            product.salesVelocityRating
        ].forEach(value => {
            const cell = document.createElement('td');
            cell.textContent = value;
            row.appendChild(cell);
        });
        tableBody.appendChild(row);
    });

    if (products.length === 0) {
        const row = document.createElement('tr');
        const cell = document.createElement('td');
        cell.colSpan = 4;
        cell.textContent = 'No forecast data available.';
        row.appendChild(cell);
        tableBody.appendChild(row);
    }
}

/* =========================
   PAGE LOAD
========================= */
document.addEventListener('DOMContentLoaded', loadForecast);
