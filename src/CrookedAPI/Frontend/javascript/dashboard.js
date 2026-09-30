const apiBase = window.location.origin.startsWith('http')
    ? window.location.origin
    : 'http://127.0.0.1:5055';

/* =========================
   CUSTOM DIALOG BOX
========================= */
function showDialog(title, message, onConfirm, onCancel) {
    const existing = document.getElementById('customDialog');
    if (existing) existing.remove();

    const overlay = document.createElement('div');
    overlay.id = 'customDialog';
    overlay.style.cssText = `
        position: fixed;
        top: 0;
        left: 0;
        width: 100%;
        height: 100%;
        background: rgba(0,0,0,0.6);
        z-index: 9999;
        display: flex;
        align-items: center;
        justify-content: center;
        font-family: Arial, sans-serif;
    `;

    const card = document.createElement('div');
    card.style.cssText = `
        background: var(--card-bg, #1a1a1a);
        color: var(--text-color, #ffffff);
        padding: 28px 32px;
        border-radius: 12px;
        min-width: 320px;
        max-width: 90vw;
        text-align: center;
        box-shadow: 0 8px 32px rgba(0,0,0,0.5);
    `;

    const titleEl = document.createElement('h3');
    titleEl.id = 'customDialogTitle';
    titleEl.style.cssText = 'margin: 0 0 12px; font-size: 1.1rem; font-weight: 600;';
    titleEl.textContent = title;

    card.setAttribute('role', 'dialog');
    card.setAttribute('aria-modal', 'true');
    card.setAttribute('aria-labelledby', titleEl.id);

    const msgEl = document.createElement('p');
    msgEl.style.cssText = 'margin: 0 0 20px; font-size: 0.95rem; line-height: 1.5; opacity: 0.9;';
    msgEl.textContent = message;

    const buttons = document.createElement('div');
    buttons.style.cssText = 'display: flex; gap: 10px; justify-content: center;';

    const okBtn = document.createElement('button');
    okBtn.textContent = onConfirm ? 'OK' : 'Close';
    okBtn.style.cssText = `
        padding: 10px 24px;
        border: none;
        border-radius: 6px;
        background: #000;
        color: #fff;
        font-weight: 600;
        cursor: pointer;
        font-size: 0.9rem;
    `;
    okBtn.addEventListener('click', () => {
        overlay.remove();
        if (onConfirm) onConfirm();
    });

    const cancelBtn = document.createElement('button');
    cancelBtn.textContent = 'Cancel';
    cancelBtn.style.cssText = `
        padding: 10px 24px;
        border: none;
        border-radius: 6px;
        background: #f0f0f0;
        color: #000;
        font-weight: 600;
        cursor: pointer;
        font-size: 0.9rem;
    `;
    cancelBtn.addEventListener('click', () => {
        overlay.remove();
        if (onCancel) onCancel();
    });

    if (onConfirm) {
        buttons.appendChild(cancelBtn);
        buttons.appendChild(okBtn);
    } else {
        buttons.appendChild(okBtn);
    }

    card.appendChild(titleEl);
    card.appendChild(msgEl);
    card.appendChild(buttons);
    overlay.appendChild(card);
    document.body.appendChild(overlay);

    overlay.addEventListener('click', (e) => {
        if (e.target === overlay) {
            overlay.remove();
            if (onCancel) onCancel();
        }
    });
}

function showToast(message, isError) {
    showDialog(isError ? 'Error' : 'Notice', message);
}

function limitAmountInput(input) {
    const parts = input.value.replace(/[^\d.]/g, '').split('.');
    const wholeAmount = parts.shift().slice(0, 5);
    const decimalAmount = parts.join('').slice(0, 2);
    input.value = parts.length || input.value.includes('.')
        ? `${wholeAmount}.${decimalAmount}`
        : wholeAmount;
}

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

        if (sectionId === 'view-products') fetchProducts();
        if (sectionId === 'view-inventory') loadInventory();
        if (sectionId === 'view-transactions') loadTransactions();
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
            const isOut = item.stock_quantity === 0;
            const isLow = item.stock_quantity <= 5 && item.stock_quantity > 0;
            const status = isOut
            ? '<span style="color:#ff0000;font-weight:bold;">OUT OF STOCK</span>'
            : isLow
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
            showToast(`Server error: ${response.status}`, true);
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
            showToast('JSON Parse Error. Raw Response: ' + text, true);
        }

    } catch (error) {
        showToast('Network Error fetching products.', true);
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
                        <button onclick="${s.isActive ? `archiveStaff(${s.id})` : `unarchiveStaff(${s.id})`}"
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
   ARCHIVE STAFF
========================= */
async function archiveStaff(id) {
    const staff = document.getElementById('staffTableBody');
    if (!staff) return;

    const row = [...staff.children].find(r => r.querySelector(`button[onclick="archiveStaff(${id})"]`));
    const nameCell = row ? row.querySelector('td:first-child') : null;
    const staffName = nameCell ? nameCell.textContent.trim() : 'this staff member';

    showDialog(
        'Archive Staff Member',
        `Are you sure you want to archive "${staffName}"?`,
        async () => {
            try {
                const response = await fetch(`${apiBase}/api/Auth/toggle-archive/${id}?archive=true`, {
                    method: 'POST'
                });

                if (response.ok) {
                    showDialog('Staff Archived', `"${staffName}" has been archived.`);
                    fetchStaff();
                } else {
                    const errorData = await response.json().catch(() => ({}));
                    showDialog('Archive Failed', errorData.message || 'Failed to archive staff member.');
                }
            } catch (error) {
                console.error('Archive Staff Error:', error);
                showDialog('Connection Error', 'Cannot connect to the server.');
            }
        }
    );
}

async function unarchiveStaff(id) {
    const staff = document.getElementById('archiveTableBody');
    if (!staff) return;

    const row = [...staff.children].find(r => r.querySelector(`button[onclick="unarchiveStaff(${id})"]`));
    const nameCell = row ? row.querySelector('td:first-child') : null;
    const staffName = nameCell ? nameCell.textContent.trim() : 'this staff member';

    showDialog(
        'Unarchive Staff Member',
        `Are you sure you want to unarchive "${staffName}"?`,
        async () => {
            try {
                const response = await fetch(`${apiBase}/api/Auth/toggle-archive/${id}?archive=false`, {
                    method: 'POST'
                });

                if (response.ok) {
                    showDialog('Staff Restored', `"${staffName}" has been unarchived.`);
                    fetchStaff();
                } else {
                    const errorData = await response.json().catch(() => ({}));
                    showDialog('Restore Failed', errorData.message || 'Failed to unarchive staff member.');
                }
            } catch (error) {
                console.error('Unarchive Staff Error:', error);
                showDialog('Connection Error', 'Cannot connect to the server.');
            }
        }
    );
}

/* =========================
   ARCHIVE PRODUCT
========================= */
async function archiveProduct(id) {
    const grid = document.getElementById('productGrid');
    if (!grid) return;

    const card = [...grid.children].find(c => c.querySelector(`button[onclick="archiveProduct(${id})"]`));
    const nameEl = card ? card.querySelector('h3') : null;
    const productName = nameEl ? nameEl.textContent.trim() : 'this product';

    showDialog(
        'Archive Product',
        `Are you sure you want to archive "${productName}"?`,
        async () => {
            try {
                const response = await fetch(`${apiBase}/api/Products/archive-product/${id}`, {
                    method: 'POST'
                });

                if (response.ok) {
                    showDialog('Product Archived', `"${productName}" has been archived.`);
                    await fetchProducts();
                    await loadInventory();
                } else {
                    const errorData = await response.json().catch(() => ({}));
                    showDialog('Archive Failed', errorData.message || 'Failed to archive product.');
                }
            } catch (error) {
                console.error('Archive Product Error:', error);
                showDialog('Connection Error', 'Cannot connect to the server.');
            }
        }
    );
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
    const card = document.getElementById('profileCard');
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
            'Pending': 'status-pending',
            'Voided': 'status-voided'
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
    const grid = document.getElementById('productGrid');
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
                <span>Stock: <strong style="color:${p.stock_quantity === 0 ? '#ff4d4d' : p.stock_quantity <= 5 ? '#ff4d4d' : '#000'}">${p.stock_quantity}</strong></span>
            </div>

            ${ownerActions}
        `;
        grid.appendChild(card);
    });
}

function openAddModal() { document.getElementById('addProductModal').style.display = 'flex'; }
function closeAddModal() { document.getElementById('addProductModal').style.display = 'none'; }

async function saveProduct(event) {
    event.preventDefault();

    const priceInput = document.getElementById('prodPrice');
    const priceText = priceInput.value.trim();
    if (!/^\d{1,5}(?:\.\d{0,2})?$/.test(priceText)) {
        showDialog('Invalid Price', 'Enter a price with up to 5 whole-number digits and 2 decimal places.');
        return;
    }

    const formData = new FormData();
    formData.append("ProductName", document.getElementById('prodName').value);
    formData.append("Category", document.getElementById('prodCategory').value);
    formData.append("Price", priceText);
    formData.append("StockQuantity", document.getElementById('prodStock').value);
    formData.append("Size", document.getElementById('prodSize').value);
    formData.append("Color", document.getElementById('prodColor').value);

    const imageFile = document.getElementById('prodImage').files[0];
    if (imageFile) formData.append("ImageFile", imageFile);

    try {
        const response = await fetch(`${apiBase}/api/Products/add-product`, {
            method: 'POST',
            body: formData
        });

        if (response.ok) {
            showToast("Product successfully added to Crooked Clothing Shop!", false);
            closeAddModal();
            document.getElementById('productForm').reset();
            fetchProducts();
        } else {
            const errorText = await response.text();
            console.error("Backend Error:", errorText);
            showToast("Error adding product: " + errorText, true);
        }
    } catch (error) {
        console.error("Network Error:", error);
        showToast("Cannot connect to the server. Check if XAMPP and VS are running.", true);
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
                if (text.includes('STAFF MANAGEMENT') || text.includes('SALES HISTORY')) {
                item.style.display = 'none';
        }
        const staffStats = document.getElementById('staff-stats-container');
            if (staffStats) {
            staffStats.style.display = 'flex';
            fetchStaffStats();
        }
        const addProductBtn = document.getElementById('addNewProductBtn');
        if (addProductBtn && role?.toLowerCase() !== 'owner') {
        addProductBtn.style.display = 'none';
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
        const outEl = document.getElementById('statOutOfStock');

        if (remainingEl) remainingEl.innerText = remaining;
        if (outEl) outEl.innerText = outOfStock;
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
        showToast('Please fill in all fields.', true);
        return;
    }

    try {
        const response = await fetch(`${apiBase}/api/Auth/register-staff`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fullName, username, password })
        });

        if (response.ok) {
            showToast('Staff account created successfully.', false);
            document.getElementById('staffName').value = '';
            document.getElementById('staffUser').value = '';
            document.getElementById('staffPass').value = '';
            fetchStaff();
        } else {
            const err = await response.text();
            showToast('Error: ' + err, true);
        }
    } catch (error) {
        console.error('Create Staff Error:', error);
        showToast('Cannot connect to the server.', true);
    }
}