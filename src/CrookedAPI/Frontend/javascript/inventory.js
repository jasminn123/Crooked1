/* =========================
   INVENTORY
========================= */
let inventoryProducts = [];

async function loadInventory() {
    try {
        const response = await fetch(`${apiBase}/api/products/get-inventory`);
        if (!response.ok) throw new Error('Inventory fetch failed');

        inventoryProducts = await response.json();
        renderInventoryTable();
    } catch (error) {
        console.error('Inventory Error:', error);
    }
}

function renderInventoryTable() {
    const tableBody = document.getElementById('inventory-list-main');
    if (!tableBody) return;

    const query = document.getElementById('inventorySearch')?.value.trim().toLocaleLowerCase() || '';
    const products = inventoryProducts.filter(item =>
        [item.product_name, item.category, item.size, item.color]
            .some(value => String(value || '').toLocaleLowerCase().includes(query))
    );
    tableBody.replaceChildren();

    if (!products.length) {
        const row = document.createElement('tr');
        const cell = document.createElement('td');
        cell.colSpan = 8;
        cell.className = 'transaction-empty';
        cell.textContent = query ? 'No inventory matches your search.' : 'No inventory products found.';
        row.appendChild(cell);
        tableBody.appendChild(row);
        return;
    }

    products.forEach(item => {
        const threshold = item.low_stock_threshold || 5;
        const isOut = item.stock_quantity === 0;
        const isLow = item.stock_quantity <= threshold && item.stock_quantity > 0;
        const status = document.createElement('span');
        status.className = isOut
            ? 'inventory-stock-out'
            : isLow
                ? 'inventory-stock-low'
                : 'inventory-stock-available';
        status.textContent = isOut ? 'OUT OF STOCK' : isLow ? 'LOW STOCK' : 'OK';

        const row = document.createElement('tr');
        [
            item.product_name,
            item.category,
            item.size || '—',
            item.color || '—',
            `₱${Number(item.price).toLocaleString('en-PH')}`,
            item.stock_quantity,
            threshold
        ].forEach((value, index) => {
            const cell = document.createElement('td');
            cell.textContent = value;
            if (index === 1) cell.className = 'col-muted';
            row.appendChild(cell);
        });

        const statusCell = document.createElement('td');
        statusCell.appendChild(status);
        row.appendChild(statusCell);
        tableBody.appendChild(row);
    });
}

document.getElementById('inventorySearch')?.addEventListener('input', renderInventoryTable);

/* =========================
   PAGE LOAD
========================= */
document.addEventListener('DOMContentLoaded', loadInventory);
