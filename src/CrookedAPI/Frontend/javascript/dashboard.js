/* =========================
   SALES ANALYTICS
========================= */
let salesChartInstance;

// Chart text and grid colours come from the theme tokens in dashboard.css.
function chartThemeColors() {
    const styles = getComputedStyle(document.documentElement);
    return {
        foreground: styles.getPropertyValue('--color-text-muted').trim(),
        grid: styles.getPropertyValue('--chart-grid').trim()
    };
}

// Darker lines on the light theme keep every series at 3:1 contrast or better.
function salesLineColor(index, alpha = 1) {
    const lightness = document.documentElement.dataset.theme === 'light' ? 32 : 62;
    return `hsla(${(index * 137.5) % 360}, 70%, ${lightness}%, ${alpha})`;
}

function updateSalesChartTheme() {
    if (!salesChartInstance) return;

    const { foreground, grid } = chartThemeColors();
    salesChartInstance.options.scales.x.ticks.color = foreground;
    salesChartInstance.options.scales.x.grid.color = grid;
    salesChartInstance.options.scales.y.ticks.color = foreground;
    salesChartInstance.options.scales.y.grid.color = grid;
    salesChartInstance.options.plugins.legend.labels.color = foreground;
    salesChartInstance.data.datasets.forEach((dataset, index) => {
        dataset.borderColor = salesLineColor(index);
        dataset.backgroundColor = salesLineColor(index, 0.12);
    });
    salesChartInstance.update('none');
}

window.addEventListener('themechange', updateSalesChartTheme);

// quiet: a live refresh keeps the current numbers and chart on screen until the new ones arrive.
async function loadSalesAnalytics({ quiet = false } = {}) {
    const status = document.getElementById('salesAnalyticsStatus');
    const totalProductsSold = document.getElementById('totalProductsSold');
    const chartSkeleton = quiet ? null : UI.skeleton('skeleton-block chart-skeleton');
    if (chartSkeleton) document.querySelector('.chart-canvas-wrap')?.appendChild(chartSkeleton);
    if (!quiet && totalProductsSold) UI.textSkeleton(totalProductsSold, '3em');

    try {
        const analytics = await UI.fetchJson(`${apiBase}/api/Forecasting/daily-sales`);
        const totalUnitsSold = analytics.products.reduce(
            (total, product) => total + product.dailySales.reduce((dailyTotal, units) => dailyTotal + units, 0),
            0
        );
        if (totalProductsSold) totalProductsSold.textContent = totalUnitsSold.toLocaleString('en-PH');

        // Products with no sales would only add flat lines at zero, so the chart leaves them out.
        const soldProducts = analytics.products.filter(product => product.dailySales.some(units => units > 0));
        renderSalesChart(analytics.dates, soldProducts, { animate: !quiet });
        if (status) status.textContent = soldProducts.length
            ? `Units sold per day over the last 7 days, for the ${soldProducts.length} product${soldProducts.length === 1 ? '' : 's'} that sold. Click or tap a name below the chart to hide or show its line.`
            : 'No products were sold in the last 7 days.';
    } catch (error) {
        console.error('Sales Analytics Error:', error);
        if (status) status.textContent = 'Unable to load sales analytics. Please try again later.';
        if (totalProductsSold) totalProductsSold.textContent = '—';
    } finally {
        chartSkeleton?.remove();
    }
}

function renderSalesChart(dates, products, { animate = true } = {}) {
    const canvas = document.getElementById('salesChart');
    if (!canvas) return;
    if (typeof Chart === 'undefined') {
        throw new Error('Sales chart is unavailable.');
    }

    if (salesChartInstance) salesChartInstance.destroy();

    const { foreground: chartForeground, grid: chartGrid } = chartThemeColors();
    const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const labels = dates.map(date =>
        new Date(`${date}T00:00:00`).toLocaleDateString('en-PH', { month: 'short', day: 'numeric' }));
    salesChartInstance = new Chart(canvas.getContext('2d'), {
        type: 'line',
        data: {
            labels,
            datasets: products.map((product, index) => ({
                label: UI.variantName(product.productName, product.size, product.color),
                data: product.dailySales,
                borderColor: salesLineColor(index),
                backgroundColor: salesLineColor(index, 0.12),
                borderWidth: 2,
                tension: 0.4,
                fill: false
            }))
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: prefersReducedMotion || !animate ? false : undefined,
            interaction: { mode: 'index', intersect: false },
            scales: {
                x: { ticks: { color: chartForeground }, grid: { color: chartGrid } },
                y: {
                    beginAtZero: true,
                    ticks: { color: chartForeground, precision: 0 },
                    grid: { color: chartGrid }
                }
            },
            plugins: {
                legend: {
                    display: true,
                    position: 'bottom',
                    labels: { color: chartForeground }
                }
            }
        }
    });
}

/* =========================
   REVENUE
   The dropdown picks today, the last 7 days, this month or this year.
   The server works out the dates in Philippine time and sends them back.
========================= */
let revenueRequest = 0;

function shopDateLabel(isoDate, withYear) {
    return new Date(`${isoDate}T00:00:00`).toLocaleDateString('en-PH', {
        month: 'short',
        day: 'numeric',
        ...(withYear ? { year: 'numeric' } : {})
    });
}

// "Nov 11, 2026" for one day, otherwise "Nov 5 – Nov 11, 2026".
function revenueRangeText(from, to) {
    if (from === to) return shopDateLabel(to, true);
    const sameYear = from.slice(0, 4) === to.slice(0, 4);
    return `${shopDateLabel(from, !sameYear)} – ${shopDateLabel(to, true)}`;
}

// quiet: a live refresh keeps the current amount on screen until the new one arrives.
async function loadRevenue({ quiet = false } = {}) {
    const value = document.getElementById('revenueValue');
    const range = document.getElementById('revenueRange');
    const period = document.getElementById('revenuePeriod')?.value || 'today';
    if (!value) return;

    // Only the newest request may fill the card, in case the period changes mid-request.
    const request = ++revenueRequest;
    if (!quiet) {
        UI.textSkeleton(value, '70%');
        if (range) UI.textSkeleton(range, '55%');
    }

    try {
        const data = await UI.fetchJson(
            `${apiBase}/api/POS/Transaction/revenue?period=${encodeURIComponent(period)}`);
        if (request !== revenueRequest) return;
        value.textContent = UI.formatPeso(data.revenue);
        if (range) range.textContent = revenueRangeText(data.from, data.to);
    } catch (error) {
        if (request !== revenueRequest) return;
        console.error('Revenue Error:', error);
        value.textContent = '—';
        if (range) range.textContent = 'Unable to load revenue. Please try again later.';
    }
}

/* =========================
   STAFF DASHBOARD STATS
========================= */
async function fetchStaffStats({ quiet = false } = {}) {
    const remainingEl = document.getElementById('statRemainingStock');
    const outEl = document.getElementById('statOutOfStock');
    if (!quiet) [remainingEl, outEl].forEach(element => element && UI.textSkeleton(element, '2.5em'));

    try {
        const products = await UI.fetchJson(`${apiBase}/api/products/get-inventory`);
        const remaining = products.reduce((sum, p) => sum + p.stock_quantity, 0);
        const outOfStock = products.filter(p => p.stock_quantity === 0).length;

        if (remainingEl) remainingEl.textContent = remaining.toLocaleString('en-PH');
        if (outEl) outEl.textContent = outOfStock.toLocaleString('en-PH');
    } catch (error) {
        console.error('Staff Stats Error:', error);
        [remainingEl, outEl].forEach(element => {
            if (element) element.textContent = '—';
        });
    }
}

/* =========================
   PAGE LOAD
========================= */
document.addEventListener('DOMContentLoaded', () => {
    try {
        const role = localStorage.getItem('userRole');
        const isOwner = role?.toLowerCase() === 'owner';

        // Staff see stock cards instead of the sales chart and revenue card.
        // The activity logs have their own owner-only page (activity-logs.html).
        if (role && !isOwner) {
            const staffStats = document.getElementById('staff-stats-container');
            if (staffStats) {
                staffStats.hidden = false;
                fetchStaffStats();
                Live.on(['products', 'exchanges'], () => fetchStaffStats({ quiet: true }));
            }

            const statsContainer = document.querySelector('.dashboard-stats-container');
            if (statsContainer) statsContainer.hidden = true;
        }

        // The sales chart and revenue come from owner-only endpoints.
        if (isOwner) {
            loadSalesAnalytics();
            loadRevenue();
            document.getElementById('revenuePeriod')?.addEventListener('change', () => loadRevenue());
            Live.on(['transactions', 'exchanges', 'products'], () => loadSalesAnalytics({ quiet: true }));
            Live.on(['transactions'], () => loadRevenue({ quiet: true }));
        }
    } catch (error) {
        console.error('Dashboard Init Error:', error);
    }
});
