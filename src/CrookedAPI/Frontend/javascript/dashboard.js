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
    const isOwner = localStorage.getItem('userRole')?.toLowerCase() === 'owner';
    if (!isOwner && ['view-products', 'view-forecasting'].includes(sectionId)) {
        return;
    }

    document.querySelectorAll('.content-section').forEach(sec => {
        sec.style.display = 'none';
    });

    const activeSection = document.getElementById(sectionId);

    if (activeSection) {
        activeSection.style.display = 'block';

        if (sectionId === 'view-products') fetchProducts();
        if (sectionId === 'view-inventory') loadInventory();
        if (sectionId === 'view-transactions') loadTransactions();
        if (sectionId === 'view-sales-history') loadSalesHistory();
        if (sectionId === 'view-forecasting') window.loadSalesAnalytics(false);
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
            const threshold = item.low_stock_threshold || 5;
            const isOut = item.stock_quantity === 0;
            const isLow = item.stock_quantity <= threshold && item.stock_quantity > 0;
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
                    <td style="padding:12px;color:white;">${threshold}</td>
                    <td style="padding:12px;">${status}</td>
                </tr>
            `;
        });
    } catch (error) {
        console.error('Inventory Error:', error);
    }
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
            await fetchArchivedProducts();
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

async function fetchArchivedProducts() {
    const grid = document.getElementById('archivedProductGrid');
    if (!grid) return;
    if (localStorage.getItem('userRole')?.toLowerCase() !== 'owner') {
        document.getElementById('archivedProductsSection')?.remove();
        return;
    }

    try {
        const response = await fetch(`${apiBase}/api/Products/get-archived-products`);
        if (!response.ok) throw new Error(`Archived products request failed (${response.status})`);

        const products = await response.json();
        grid.replaceChildren();
        if (products.length === 0) {
            const empty = document.createElement('p');
            empty.className = 'archived-products-empty';
            empty.textContent = 'No archived products.';
            grid.appendChild(empty);
            return;
        }

        products.forEach(product => {
            const card = document.createElement('article');
            card.className = 'archived-product-card';

            const image = document.createElement('img');
            image.src = product.image_url || '';
            image.alt = product.product_name;

            const info = document.createElement('div');
            info.className = 'archived-product-info';
            const name = document.createElement('strong');
            name.textContent = product.product_name;
            const description = document.createElement('span');
            description.textContent = `${product.category} · ₱${Number(product.price).toLocaleString('en-PH')}`;
            info.append(name, description);

            const restoreButton = document.createElement('button');
            restoreButton.type = 'button';
            restoreButton.className = 'btn-unarchive';
            restoreButton.textContent = 'Restore';
            restoreButton.addEventListener('click', () => restoreProduct(product.id, product.product_name));

            card.append(image, info, restoreButton);
            grid.appendChild(card);
        });
    } catch (error) {
        console.error('Archived Products Error:', error);
        const message = document.createElement('p');
        message.className = 'archived-products-empty';
        message.textContent = 'Unable to load archived products. Restart the API if you just added archive support, then refresh.';
        grid.replaceChildren(message);
    }
}

async function restoreProduct(id, productName) {
    showDialog(
        'Restore Product',
        `Restore "${productName}" to active products and POS?`,
        async () => {
            try {
                const response = await fetch(`${apiBase}/api/Products/restore-product/${id}`, { method: 'POST' });
                if (!response.ok) {
                    const error = await response.json().catch(() => ({}));
                    throw new Error(error.message || `Restore failed (${response.status})`);
                }

                await fetchProducts();
                await loadInventory();
                showDialog('Product Restored', `"${productName}" is active again.`);
            } catch (error) {
                console.error('Restore Product Error:', error);
                showDialog('Restore Failed', error.message || 'Unable to restore product.');
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

async function loadSalesHistory() {
    const tbody = document.getElementById('salesHistoryTableBody');
    if (!tbody) return;

    try {
        const response = await fetch(`${apiBase}/api/POS/Transaction`);
        if (!response.ok) throw new Error(`Sales history request failed (${response.status})`);
        const transactions = await response.json();

        tbody.replaceChildren();
        if (!transactions.length) {
            const row = document.createElement('tr');
            const cell = document.createElement('td');
            cell.colSpan = 5;
            cell.className = 'transaction-empty';
            cell.textContent = 'No sales recorded yet.';
            row.appendChild(cell);
            tbody.appendChild(row);
            return;
        }

        transactions.forEach(transaction => {
            const row = document.createElement('tr');
            [
                transaction.date_time,
                transaction.reference_id,
                `₱${Number(transaction.total_amount).toLocaleString('en-PH', { minimumFractionDigits: 2 })}`,
                transaction.assisted_by || 'Unknown',
                transaction.status
            ].forEach(value => {
                const cell = document.createElement('td');
                cell.textContent = value;
                row.appendChild(cell);
            });
            tbody.appendChild(row);
        });
    } catch (error) {
        console.error('Sales History Error:', error);
        tbody.replaceChildren();
        const row = document.createElement('tr');
        const cell = document.createElement('td');
        cell.colSpan = 5;
        cell.className = 'transaction-empty';
        cell.textContent = 'Unable to load sales history.';
        row.appendChild(cell);
        tbody.appendChild(row);
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
const productsById = new Map();

function renderProductGrid(products) {
    const grid = document.getElementById('productGrid');
    const userRole = localStorage.getItem('userRole')?.toLowerCase();

    if (!grid) return;
    grid.innerHTML = '';
    productsById.clear();

    products.forEach(p => {
        productsById.set(p.id, p);
        const card = document.createElement('div');
        card.className = 'product-card';

        let ownerActions = '';
        
        if (userRole === 'owner') {
            ownerActions = `
                <div style="margin-top:15px; display:flex; gap:8px; border-top:1px solid #f8f8f8; padding-top:15px;">
                    <button class="action-btn btn-edit" onclick="openEditProductModal(${p.id})" style="flex:1;">Edit</button>
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

function openEditProductModal(id) {
    const product = productsById.get(id);
    if (!product) {
        showDialog('Product Unavailable', 'Refresh the product list and try again.');
        return;
    }

    document.getElementById('editProdId').value = product.id;
    document.getElementById('editProdName').value = product.product_name || '';
    const categorySelect = document.getElementById('editProdCategory');
    const category = product.category || 'T-Shirts';
    if (![...categorySelect.options].some(option => option.value === category)) {
        categorySelect.add(new Option(category, category));
    }
    categorySelect.value = category;
    document.getElementById('editProdPrice').value = product.price;
    document.getElementById('editProdStock').value = product.stock_quantity;
    document.getElementById('editProdSize').value = product.size || '';
    document.getElementById('editProdColor').value = product.color || '';

    const preview = document.getElementById('editProdImagePreview');
    preview.src = product.image_url || '';
    preview.style.display = product.image_url ? 'block' : 'none';
    document.getElementById('editProdImage').value = '';
    document.getElementById('editProductModal').style.display = 'flex';
}

function closeEditProductModal() {
    document.getElementById('editProductModal').style.display = 'none';
}

async function saveProductEdit(event) {
    event.preventDefault();
    const priceText = document.getElementById('editProdPrice').value.trim();
    if (!/^\d{1,5}(?:\.\d{0,2})?$/.test(priceText)) {
        showDialog('Invalid Price', 'Enter a price with up to 5 whole-number digits and 2 decimal places.');
        return;
    }

    const formData = new FormData();
    formData.append('ProductName', document.getElementById('editProdName').value.trim());
    formData.append('Category', document.getElementById('editProdCategory').value);
    formData.append('Price', priceText);
    formData.append('StockQuantity', document.getElementById('editProdStock').value);
    formData.append('Size', document.getElementById('editProdSize').value);
    formData.append('Color', document.getElementById('editProdColor').value);
    const imageFile = document.getElementById('editProdImage').files[0];
    if (imageFile) formData.append('ImageFile', imageFile);

    const id = document.getElementById('editProdId').value;
    try {
        const response = await fetch(`${apiBase}/api/Products/update-product/${id}`, {
            method: 'PUT',
            body: formData
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            const validationMessages = error.errors
                ? Object.values(error.errors).flat().join(' ')
                : '';
            throw new Error(validationMessages || error.message || error.title || `Product update failed (${response.status})`);
        }

        closeEditProductModal();
        await fetchProducts();
        await loadInventory();
        showDialog('Product Updated', 'Product details were saved successfully.');
    } catch (error) {
        console.error('Update Product Error:', error);
        showDialog('Update Failed', error.message || 'Unable to update product.');
    }
}

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
    document.getElementById('editProdImage')?.addEventListener('change', event => {
        const input = event.currentTarget;
        const preview = document.getElementById('editProdImagePreview');
        const imageFile = input.files[0];
        if (!imageFile) return;

        preview.src = URL.createObjectURL(imageFile);
        preview.style.display = 'block';
    });

    try {
        // Hide all sections, show dashboard
        document.querySelectorAll('.content-section').forEach(sec => {
            sec.style.display = 'none';
        });
        document.getElementById('view-dashboard').style.display = 'block';

        fetchLogs();

        const role = localStorage.getItem('userRole');
        const name = localStorage.getItem('userName');
        const isOwner = role?.toLowerCase() === 'owner';
        const archivedProductsSection = document.getElementById('archivedProductsSection');
        if (archivedProductsSection) {
            archivedProductsSection.style.display = isOwner ? '' : 'none';
        }

        const profileName = document.querySelector('.Owner');
        if (profileName && name) profileName.innerText = name;

        const profileCardName = document.querySelector('.profile-card-header span');
        if (profileCardName && name) profileCardName.innerText = name;
        
        // Role-based UI hiding
        // After
        if (role && role.toLowerCase() !== 'owner') {
            document.getElementById('nav-products')?.remove();
            document.getElementById('nav-forecasting')?.remove();
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