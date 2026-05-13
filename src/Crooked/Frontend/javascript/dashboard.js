const apiBase = window.location.origin.startsWith('http')
    ? window.location.origin
    : 'http://127.0.0.1:5055';

/* =========================
   SECTION SWITCHING
========================= */
function showSection(sectionId, element) {
    document.querySelectorAll('.content-section').forEach(sec => {
        sec.style.display = 'none';
    });

    const activeSection = document.getElementById(sectionId);

    if (activeSection) {
        activeSection.style.display = 'block';

        if (sectionId === 'view-products')      fetchProducts();
        if (sectionId === 'view-inventory')     loadInventory();
        if (sectionId === 'view-transactions')  loadTransactions();
    }

    document.querySelectorAll('.nav-item').forEach(nav => {
        nav.classList.remove('nav-active', 'active');
    });

    if (element) {
        element.classList.add('nav-active', 'active');
        const header = document.getElementById('welcomeHeader');
        if (header) header.innerText = element.innerText;
    }
}

/* =========================
   INVENTORY
========================= */
async function loadInventory() {
    try {
        const response = await fetch(`${apiBase}/api/products/get-inventory`);
        if (!response.ok) throw new Error('Inventory fetch failed');

        const products = await response.json();
        const tableBody = document.getElementById('inventory-list-main');
        if (!tableBody) return;

        tableBody.innerHTML = '';
        products.forEach(item => {
            const isLow = item.stock_quantity <= 5;
            const status = isLow
                ? '<span style="color:#ff4d4d;font-weight:bold;">LOW STOCK</span>'
                : '<span style="color:#2ecc71;">OK</span>';

            tableBody.innerHTML += `
                <tr style="border-bottom:1px solid #222;">
                    <td style="padding:12px;color:white;">${item.product_name}</td>
                    <td style="padding:12px;color:#888;">${item.category}</td>
                    <td style="padding:12px;color:white;">₱${item.price.toLocaleString()}</td>
                    <td style="padding:12px;color:white;">${item.stock_quantity}</td>
                    <td style="padding:12px;">${status}</td>
                </tr>
            `;
        });
    } catch (error) {
        console.error('Inventory Error:', error);
    }
}

/* =========================
   SALES CHART
========================= */
function initSalesChart() {
    const canvas = document.getElementById('salesChart');
    if (!canvas || typeof Chart === 'undefined') return;

    const ctx = canvas.getContext('2d');
    new Chart(ctx, {
        type: 'line',
        data: {
            labels: ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'],
            datasets: [{
                label: 'Sales',
                data: [1200, 1900, 800, 1500, 2200, 3000, 2500],
                borderColor: '#ff0000',
                backgroundColor: 'rgba(255,0,0,0.08)',
                borderWidth: 2,
                tension: 0.4,
                fill: true
            }]
        },
        options: {
            responsive: true,
            plugins: { legend: { display: false } }
        }
    });
}

/* =========================
   PRODUCTS
========================= */
async function fetchProducts() {
    try {
        const response = await fetch(`${apiBase}/api/Products/get-products`);

        if (!response.ok) {
            console.error(`Server error: ${response.status}`);
            return;
        }

        const text = await response.text();
        if (!text || text.trim().length === 0) {
            console.warn('Products API returned empty response.');
            return;
        }

        try {
            const products = JSON.parse(text);
            if (typeof renderProductGrid === 'function') {
                renderProductGrid(products);
            }
        } catch (parseError) {
            console.error('JSON Parse Error. Raw Response:', text);
        }

    } catch (error) {
        console.error('Network Error fetching products:', error);
    }
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
   STAFF
========================= */
async function fetchStaff() {
    try {
        const response = await fetch(`${apiBase}/api/Auth/get-staff`);
        if (!response.ok) return;
        const staff = await response.json();

        const staffBody   = document.getElementById('staffTableBody');
        const archiveBody = document.getElementById('archiveTableBody');
        if (!staffBody || !archiveBody) return;

        let activeHtml  = '';
        let archiveHtml = '';

        staff.forEach(s => {
            const row = `
                <tr>
                    <td>${s.fullName}</td>
                    <td>${s.username}</td>
                    <td>
                        <button onclick="archiveStaff(${s.id})"
                            class="${s.isActive ? 'btn-archive' : 'btn-unarchive'}">
                            ${s.isActive ? 'Archive' : 'Unarchive'}
                        </button>
                    </td>
                </tr>
            `;
            s.isActive ? activeHtml += row : archiveHtml += row;
        });

        staffBody.innerHTML   = activeHtml;
        archiveBody.innerHTML = archiveHtml;
    } catch (error) {
        console.error('Staff Error:', error);
    }
}

/* =========================
   PROFILE & LOGOUT
========================= */
function toggleProfileCard(event) {
    event.stopPropagation();
    const card = document.getElementById('profileCard');
    if (card) card.style.display = card.style.display === 'block' ? 'none' : 'block';
}

document.addEventListener('click', (event) => {
    const card          = document.getElementById('profileCard');
    const profileSection = document.querySelector('.profile-section');
    if (card && profileSection && !profileSection.contains(event.target)) {
        card.style.display = 'none';
    }
});

function logout() {
    localStorage.clear();
    window.location.href = 'index.html';
}

/* =========================
   TRANSACTION LOGS
========================= */
async function loadTransactions() {
    try {
        const response = await fetch(`${apiBase}/api/POS/Transaction`);
        if (!response.ok) throw new Error('Transaction fetch failed');

        const transactions = await response.json();
        renderTransactionTable(transactions);
    } catch (error) {
        console.error('Transaction Error:', error);
    }
}

function renderTransactionTable(transactions) {
    const tbody = document.getElementById('transactionTableBody');
    if (!tbody) return;

    if (!transactions || !transactions.length) {
        tbody.innerHTML = `<tr><td colspan="5" class="transaction-empty">No transactions found.</td></tr>`;
        return;
    }

    tbody.innerHTML = transactions.map(txn => {
        const statusClass = {
            'Completed': 'status-completed',
            'Pending':   'status-pending',
            'Voided':    'status-voided'
        }[txn.status] || '';

        return `
        <tr>
            <td>${txn.transaction_id}</td>
            <td class="col-muted">${txn.date_time}</td>
            <td>₱${Number(txn.total_amount).toLocaleString('en-PH', { minimumFractionDigits: 2 })}</td>
            <td class="col-muted">${txn.reference_id}</td>
            <td class="${statusClass}">${txn.status}</td>
        </tr>`;
    }).join('');
}

/* =========================
   PRODUCT MANAGEMENT
========================= */
function renderProductGrid(products) {
    const grid     = document.getElementById('productGrid');
    const userRole = localStorage.getItem('userRole')?.toLowerCase();

    if (!grid) return;
    grid.innerHTML = '';

    products.forEach(p => {
        const card = document.createElement('div');
        card.className = 'product-card';

        let ownerActions = '';
        if (userRole === 'owner') {
            ownerActions = `
                <div style="margin-top:15px; display:flex; gap:8px; border-top:1px solid #f8f8f8; padding-top:15px;">
                    <button class="action-btn btn-edit" onclick="editProduct(${p.id})" style="flex:1;">Edit</button>
                    <button class="action-btn btn-archive" onclick="archiveProduct(${p.id})" style="flex:1;">Archive</button>
                </div>
            `;
        }

        card.innerHTML = `
            <div class="img-placeholder">
                ${p.image_url
                    ? `<img src="${p.image_url}" style="width:100%; height:100%; object-fit:cover; border-radius:8px;">`
                    : 'NO IMAGE AVAILABLE'}
            </div>

            <div style="display:flex; justify-content:space-between; align-items:center;">
                <span style="font-size:11px; font-weight:700; color:#2ecc71; text-transform:uppercase;">${p.category}</span>
                <span style="font-weight:700; font-size:16px;">₱${p.price.toLocaleString()}</span>
            </div>

            <h3 style="margin:8px 0; font-size:18px; color:#000;">${p.product_name}</h3>

            <div style="display:flex; gap:10px; font-size:12px; color:#666;">
                <span>Size: <strong>${p.size || '-'}</strong></span>
                <span>Color: <strong>${p.color || '-'}</strong></span>
                <span>Stock: <strong style="color:${p.stock_quantity <= 5 ? '#ff4d4d' : '#000'}">${p.stock_quantity}</strong></span>
            </div>

            ${ownerActions}
        `;
        grid.appendChild(card);
    });
}

function openAddModal()  { document.getElementById('addProductModal').style.display = 'flex'; }
function closeAddModal() { document.getElementById('addProductModal').style.display = 'none'; }

async function saveProduct(event) {
    event.preventDefault();

    const formData = new FormData();
    formData.append("ProductName",    document.getElementById('prodName').value);
    formData.append("Category",       document.getElementById('prodCategory').value);
    formData.append("Price",          document.getElementById('prodPrice').value);
    formData.append("StockQuantity",  document.getElementById('prodStock').value);
    formData.append("Size",           document.getElementById('prodSize').value);
    formData.append("Color",          document.getElementById('prodColor').value);

    const imageFile = document.getElementById('prodImage').files[0];
    if (imageFile) formData.append("ImageFile", imageFile);

    try {
        const response = await fetch(`${apiBase}/api/Products/add-product`, {
            method: 'POST',
            body: formData
        });

        if (response.ok) {
            alert("Product successfully added to Crooked Clothing Shop!");
            closeAddModal();
            document.getElementById('productForm').reset();
            fetchProducts();
        } else {
            const errorText = await response.text();
            console.error("Backend Error:", errorText);
            alert("Error adding product: " + errorText);
        }
    } catch (error) {
        console.error("Network Error:", error);
        alert("Cannot connect to the server. Check if XAMPP and VS are running.");
    }
}

function filterProducts() {
    const input = document.getElementById('productSearch').value.toLowerCase();
    const cards = document.querySelectorAll('.product-card');
    cards.forEach(card => {
        card.style.display = card.innerText.toLowerCase().includes(input) ? 'block' : 'none';
    });
}

function filterBycategory(category) {
    document.querySelectorAll('.tab-btn').forEach(btn => btn.classList.remove('active'));
    event.target.classList.add('active');

    const cards = document.querySelectorAll('.product-card');
    cards.forEach(card => {
        card.style.display = (category === 'All' || card.innerText.includes(category)) ? 'block' : 'none';
    });
}

/* =========================
   PAGE LOAD
========================= */
document.addEventListener('DOMContentLoaded', () => {
    try {
        // Hide all sections, show dashboard
        document.querySelectorAll('.content-section').forEach(sec => {
            sec.style.display = 'none';
        });
        document.getElementById('view-dashboard').style.display = 'block';

        initSalesChart();
        fetchLogs();

        const role = localStorage.getItem('userRole');
        const name = localStorage.getItem('userName');

        const profileName = document.querySelector('.Owner');
        if (profileName && name) profileName.innerText = name;

        const profileCardName = document.querySelector('.profile-card-header span');
        if (profileCardName && name) profileCardName.innerText = name;
        
        // Role-based UI hiding
        // After
        if (role && role.toLowerCase() !== 'owner') {
            document.querySelectorAll('.nav-item').forEach(item => {
                const text = item.innerText.toUpperCase();
                if (text.includes('STAFF MANAGEMENT') || text.includes('INVENTORY') || text.includes('SALES HISTORY')) {
                item.style.display = 'none';
        }
        const staffStats = document.getElementById('staff-stats-container');
            if (staffStats) {
            staffStats.style.display = 'flex';
            fetchStaffStats();
        }
    });

    // Hide sales chart and revenue card from staff
    const statsContainer = document.querySelector('.dashboard-stats-container');
    if (statsContainer) statsContainer.style.display = 'none';
}

        if (typeof fetchStaff === 'function') fetchStaff();

    } catch (error) {
        console.error('Dashboard Init Error:', error);
    }
});

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
        const outEl       = document.getElementById('statOutOfStock');

        if (remainingEl) remainingEl.innerText = remaining;
        if (outEl)       outEl.innerText       = outOfStock;
    } catch (error) {
        console.error('Staff Stats Error:', error);
    }
}

/* =========================
   ADD STAFF
========================= */
async function addStaff() {
    const fullName = document.getElementById('staffName').value.trim();
    const username = document.getElementById('staffUser').value.trim();
    const password = document.getElementById('staffPass').value.trim();

    if (!fullName || !username || !password) {
        alert('Please fill in all fields.');
        return;
    }

    try {
        const response = await fetch(`${apiBase}/api/Auth/register-staff`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fullName, username, password })
        });

        if (response.ok) {
            alert('Staff account created successfully.');
            document.getElementById('staffName').value = '';
            document.getElementById('staffUser').value = '';
            document.getElementById('staffPass').value = '';
            fetchStaff();
        } else {
            const err = await response.text();
            alert('Error: ' + err);
        }
    } catch (error) {
        console.error('Create Staff Error:', error);
        alert('Cannot connect to the server.');
    }
}