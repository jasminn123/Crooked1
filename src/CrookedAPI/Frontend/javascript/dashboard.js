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

async function loadSalesAnalytics() {
    const status = document.getElementById('salesAnalyticsStatus');
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
        const totalProductsSold = document.getElementById('totalProductsSold');
        if (totalProductsSold) totalProductsSold.textContent = totalUnitsSold.toLocaleString('en-PH');

        const todayRevenue = document.getElementById('todayRevenue');
        if (todayRevenue) {
            todayRevenue.textContent = Number(revenueData.revenue).toLocaleString('en-PH', {
                style: 'currency',
                currency: 'PHP',
                minimumFractionDigits: 2
            });
        }

        renderSalesChart(analytics);
        if (status) status.textContent = analytics.products.length
            ? 'Units sold per active product, per day, over the last 7 days.'
            : 'No active products or sales data to display.';
    } catch (error) {
        console.error('Sales Analytics Error:', error);
        if (status) status.textContent = 'Unable to load sales analytics. Please try again later.';
    }
}

function renderSalesChart(analytics) {
    const canvas = document.getElementById('salesChart');
    if (!canvas) return;
    if (typeof Chart === 'undefined') {
        throw new Error('Sales chart is unavailable.');
    }

    if (salesChartInstance) salesChartInstance.destroy();

    const { foreground: chartForeground, grid: chartGrid } = chartThemeColors();
    const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const labels = analytics.dates.map(date =>
        new Date(`${date}T00:00:00`).toLocaleDateString('en-PH', { month: 'short', day: 'numeric' }));
    salesChartInstance = new Chart(canvas.getContext('2d'), {
        type: 'line',
        data: {
            labels,
            datasets: analytics.products.map((product, index) => ({
                label: product.productName,
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
            animation: prefersReducedMotion ? false : undefined,
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
async function fetchLogs() {
    try {
        const response = await fetch(`${apiBase}/api/Auth/get-logs`);
        if (!response.ok) return;
        const logs = await response.json();

        const logBody = document.getElementById('logTableBody');
        if (!logBody) return;

        logBody.innerHTML = logs.map(log => {
            const date = new Date(log.dateOccurred).toLocaleString();
            return `
                <tr>
                    <td>${log.staffName}</td>
                    <td>${log.action}</td>
                    <td>${date}</td>
                </tr>
            `;
        }).join('');
    } catch (error) {
        console.error('Logs Error:', error);
    }
}

/* =========================
   STAFF DASHBOARD STATS
========================= */
async function fetchStaffStats() {
    try {
        const response = await fetch(`${apiBase}/api/products/get-inventory`);
        if (!response.ok) return;

        const products = await response.json();
        const remaining = products.reduce((sum, p) => sum + p.stock_quantity, 0);
        const outOfStock = products.filter(p => p.stock_quantity === 0).length;

        const remainingEl = document.getElementById('statRemainingStock');
        const outEl = document.getElementById('statOutOfStock');

        if (remainingEl) remainingEl.innerText = remaining;
        if (outEl) outEl.innerText = outOfStock;
    } catch (error) {
        console.error('Staff Stats Error:', error);
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
            }

            const statsContainer = document.querySelector('.dashboard-stats-container');
            if (statsContainer) statsContainer.style.display = 'none';
        }

        fetchLogs();
        loadSalesAnalytics();
    } catch (error) {
        console.error('Dashboard Init Error:', error);
    }
});
