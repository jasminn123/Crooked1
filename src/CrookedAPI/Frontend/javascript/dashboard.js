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
    const todayRevenue = document.getElementById('todayRevenue');
    const chartSkeleton = quiet ? null : UI.skeleton('skeleton-block chart-skeleton');
    if (chartSkeleton) document.querySelector('.chart-canvas-wrap')?.appendChild(chartSkeleton);
    if (!quiet && totalProductsSold) UI.textSkeleton(totalProductsSold, '3em');
    if (!quiet && todayRevenue) UI.textSkeleton(todayRevenue, '70%');

    try {
        const [salesResponse, revenueResponse] = await Promise.all([
            fetch(`${apiBase}/api/Forecasting/daily-sales`),
            fetch(`${apiBase}/api/POS/Transaction/today-revenue`)
        ]);
        if (!salesResponse.ok) throw new Error(`Daily sales request failed (${salesResponse.status})`);
        if (!revenueResponse.ok) throw new Error(`Today's revenue request failed (${revenueResponse.status})`);

        const [analytics, revenueData] = await Promise.all([
            salesResponse.json(),
            revenueResponse.json()
        ]);
        const totalUnitsSold = analytics.products.reduce(
            (total, product) => total + product.dailySales.reduce((dailyTotal, units) => dailyTotal + units, 0),
            0
        );
        if (totalProductsSold) totalProductsSold.textContent = totalUnitsSold.toLocaleString('en-PH');

        if (todayRevenue) {
            todayRevenue.textContent = Number(revenueData.revenue).toLocaleString('en-PH', {
                style: 'currency',
                currency: 'PHP',
                minimumFractionDigits: 2
            });
        }

        // Products with no sales would only add flat lines at zero, so the chart leaves them out.
        const soldProducts = analytics.products.filter(product => product.dailySales.some(units => units > 0));
        renderSalesChart(analytics.dates, soldProducts, { animate: !quiet });
        if (status) status.textContent = soldProducts.length
            ? `Units sold per day over the last 7 days, for the ${soldProducts.length} product${soldProducts.length === 1 ? '' : 's'} that sold. Click a name below the chart to hide or show its line.`
            : 'No products were sold in the last 7 days.';
    } catch (error) {
        console.error('Sales Analytics Error:', error);
        if (status) status.textContent = 'Unable to load sales analytics. Please try again later.';
        if (totalProductsSold) totalProductsSold.textContent = '—';
        if (todayRevenue) todayRevenue.textContent = '—';
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
   ACTIVITY LOGS
========================= */
async function fetchLogs({ quiet = false } = {}) {
    const logBody = document.getElementById('logTableBody');
    if (!logBody) return;
    if (!quiet) UI.tableSkeleton(logBody, 3, 5);

    try {
        const logs = await UI.fetchJson(`${apiBase}/api/Auth/get-logs`);
        if (!logs.length) {
            UI.tableMessage(logBody, 3, {
                icon: 'fa-clock-rotate-left',
                title: 'No activity yet',
                message: 'Logins, exchanges and imports will show up here.'
            });
            return;
        }

        UI.fillTable(logBody, logs.map(log => {
            const row = document.createElement('tr');
            row.append(
                UI.cell(log.staffName),
                UI.cell(log.action),
                UI.cell(new Date(log.dateOccurred).toLocaleString(), 'col-muted')
            );
            return row;
        }), { fade: !quiet });
    } catch (error) {
        console.error('Logs Error:', error);
        UI.tableMessage(logBody, 3, {
            isError: true,
            title: 'Unable to load activity logs',
            message: error.message,
            actionLabel: 'Try again',
            onAction: () => fetchLogs()
        });
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

        // Staff see stock cards instead of the sales chart and revenue card
        if (role && !isOwner) {
            const staffStats = document.getElementById('staff-stats-container');
            if (staffStats) {
                staffStats.style.display = 'flex';
                fetchStaffStats();
                Live.on(['products', 'exchanges'], () => fetchStaffStats({ quiet: true }));
            }

            const statsContainer = document.querySelector('.dashboard-stats-container');
            if (statsContainer) statsContainer.style.display = 'none';
        }

        fetchLogs();
        loadSalesAnalytics();
        Live.on(['activity'], () => fetchLogs({ quiet: true }));
        Live.on(['transactions', 'exchanges', 'products'], () => loadSalesAnalytics({ quiet: true }));
    } catch (error) {
        console.error('Dashboard Init Error:', error);
    }
});
