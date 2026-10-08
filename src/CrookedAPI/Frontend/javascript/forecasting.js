(() => {
    const apiBase = window.location.origin.startsWith('http')
        ? window.location.origin
        : 'http://127.0.0.1:5055';
    let salesChartInstance;

    async function loadSalesAnalytics(updateChart = true) {
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

            renderSalesAnalytics(analytics, updateChart);
            if (status) status.textContent = analytics.products.length
                ? 'Units sold per active product, per day, over the last 7 days.'
                : 'No active products or sales data to display.';
        } catch (error) {
            console.error('Sales Analytics Error:', error);
            if (status) status.textContent = 'Unable to load sales analytics. Please try again later.';
        }
    }

    function renderSalesAnalytics(analytics, updateChart) {
        renderForecastTable(analytics.products);
        if (!updateChart) return;

        const canvas = document.getElementById('salesChart');
        if (!canvas || typeof Chart === 'undefined') {
            throw new Error('Sales chart is unavailable.');
        }

        if (salesChartInstance) salesChartInstance.destroy();

        const labels = analytics.dates.map(date =>
            new Date(`${date}T00:00:00`).toLocaleDateString('en-PH', { month: 'short', day: 'numeric' }));
        salesChartInstance = new Chart(canvas.getContext('2d'), {
            type: 'line',
            data: {
                labels,
                datasets: analytics.products.map((product, index) => ({
                    label: product.productName,
                    data: product.dailySales,
                    borderColor: `hsl(${(index * 137.5) % 360}, 70%, 58%)`,
                    backgroundColor: `hsla(${(index * 137.5) % 360}, 70%, 58%, 0.12)`,
                    borderWidth: 2,
                    tension: 0.4,
                    fill: false
                }))
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: { mode: 'index', intersect: false },
                scales: {
                    x: { ticks: { color: '#ddd' }, grid: { color: 'rgba(255,255,255,0.08)' } },
                    y: {
                        beginAtZero: true,
                        ticks: { color: '#ddd', precision: 0 },
                        grid: { color: 'rgba(255,255,255,0.08)' }
                    }
                },
                plugins: {
                    legend: {
                        display: true,
                        position: 'bottom',
                        labels: { color: '#ddd' }
                    }
                }
            }
        });
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

    function renderForecastNotifications(notifications) {
        const list = document.getElementById('forecastNotificationList');
        if (!list) return;

        list.replaceChildren();
        if (!notifications.length) {
            const empty = document.createElement('p');
            empty.className = 'forecast-notification-empty';
            empty.textContent = 'No forecast notifications yet.';
            list.appendChild(empty);
            return;
        }

        notifications.forEach(notification => {
            const item = document.createElement('article');
            item.className = `forecast-notification-item${notification.isRead ? '' : ' unread'}`;

            const image = document.createElement('img');
            image.className = 'forecast-notification-image';
            image.alt = '';
            image.src = notification.imageUrl || '';
            image.addEventListener('error', () => {
                image.style.visibility = 'hidden';
            }, { once: true });

            const copy = document.createElement('div');
            copy.className = 'forecast-notification-copy';
            const name = document.createElement('strong');
            name.textContent = notification.productName;
            copy.appendChild(name);

            if (notification.oldThreshold !== notification.newThreshold) {
                const threshold = document.createElement('span');
                const direction = notification.newThreshold > notification.oldThreshold ? 'increased' : 'decreased';
                threshold.textContent = `Low-stock threshold ${direction}: ${notification.oldThreshold} to ${notification.newThreshold}`;
                copy.appendChild(threshold);
            }

            if (notification.oldVelocity !== notification.newVelocity) {
                const velocity = document.createElement('span');
                velocity.textContent = `Sales velocity: ${notification.oldVelocity} to ${notification.newVelocity}`;
                copy.appendChild(velocity);
            }

            const time = document.createElement('time');
            const createdAt = new Date(notification.createdAt);
            time.dateTime = createdAt.toISOString();
            time.textContent = createdAt.toLocaleString();
            copy.appendChild(time);
            item.append(image, copy);
            list.appendChild(item);
        });
    }

    async function loadForecastNotifications(markRead = false) {
        const response = await fetch(`${apiBase}/api/Forecasting/notifications`);
        if (!response.ok) throw new Error(`Notifications request failed (${response.status})`);

        const data = await response.json();
        renderForecastNotifications(data.notifications || []);
        const count = document.getElementById('forecastNotificationCount');
        if (count) {
            count.textContent = data.unreadCount > 99 ? '99+' : String(data.unreadCount || 0);
            count.style.display = data.unreadCount > 0 ? 'block' : 'none';
        }

        if (markRead && data.unreadCount > 0) {
            const readResponse = await fetch(`${apiBase}/api/Forecasting/notifications/read`, { method: 'POST' });
            if (!readResponse.ok) throw new Error(`Marking notifications read failed (${readResponse.status})`);
            document.querySelectorAll('.forecast-notification-item.unread').forEach(item => {
                item.classList.remove('unread');
            });
            if (count) count.style.display = 'none';
        }
    }

    function initializeForecastNotifications() {
        const container = document.getElementById('forecastNotifications');
        const button = document.getElementById('forecastNotificationButton');
        const panel = document.getElementById('forecastNotificationPanel');
        if (!container || !button || !panel || localStorage.getItem('userRole')?.toLowerCase() !== 'owner') return;

        container.style.display = '';
        loadForecastNotifications().catch(error => {
            console.error('Forecast Notifications Error:', error);
        });
        window.setInterval(() => {
            loadForecastNotifications().catch(error => {
                console.error('Forecast Notifications Error:', error);
            });
        }, 60000);

        button.addEventListener('click', async event => {
            event.stopPropagation();
            const willOpen = panel.style.display === 'none';
            panel.style.display = willOpen ? 'block' : 'none';
            button.setAttribute('aria-expanded', String(willOpen));
            if (!willOpen) return;

            const list = document.getElementById('forecastNotificationList');
            if (list) list.textContent = 'Loading notifications...';
            try {
                await loadForecastNotifications(true);
            } catch (error) {
                console.error('Forecast Notifications Error:', error);
                if (list) list.textContent = 'Could not load forecast notifications. Please try again.';
            }
        });
        document.addEventListener('click', event => {
            if (!container.contains(event.target)) {
                panel.style.display = 'none';
                button.setAttribute('aria-expanded', 'false');
            }
        });
    }

    window.loadSalesAnalytics = loadSalesAnalytics;
    document.addEventListener('DOMContentLoaded', () => {
        loadSalesAnalytics();
        initializeForecastNotifications();
    });
})();
