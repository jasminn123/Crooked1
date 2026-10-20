/* =========================
   INVENTORY
========================= */
const INVENTORY_COLUMNS = 8;
let inventoryProducts = [];

async function loadInventory() {
    const tableBody = document.getElementById('inventory-list-main');
    if (!tableBody) return;
    UI.tableSkeleton(tableBody, INVENTORY_COLUMNS, 8);

    try {
        inventoryProducts = await UI.fetchJson(`${apiBase}/api/products/get-inventory`);
        renderInventoryTable({ fade: true });
    } catch (error) {
        console.error('Inventory Error:', error);
        UI.tableMessage(tableBody, INVENTORY_COLUMNS, {
            isError: true,
            title: 'Unable to load inventory',
            message: error.message,
            actionLabel: 'Try again',
            onAction: loadInventory
        });
    }
}

function renderInventoryTable({ fade = false } = {}) {
    const tableBody = document.getElementById('inventory-list-main');
    if (!tableBody) return;

    const query = document.getElementById('inventorySearch')?.value.trim().toLocaleLowerCase() || '';
    const products = inventoryProducts.filter(item =>
        [item.product_name, item.category, item.size, item.color]
            .some(value => String(value || '').toLocaleLowerCase().includes(query))
    );

    if (!products.length) {
        UI.tableMessage(tableBody, INVENTORY_COLUMNS, query
            ? {
                icon: 'fa-magnifying-glass',
                title: 'No matching products',
                message: 'Try a different product name, category, size or color.'
            }
            : {
                icon: 'fa-boxes-stacked',
                title: 'No inventory yet',
                message: 'Products you add on the Products page will be listed here.'
            });
        return;
    }

    UI.fillTable(tableBody, products.map(item => {
        const threshold = item.low_stock_threshold || 5;
        const isOut = item.stock_quantity === 0;
        const isLow = item.stock_quantity <= threshold && item.stock_quantity > 0;
        const status = isOut
            ? UI.badge('Out of stock', 'danger')
            : isLow
                ? UI.badge('Low stock', 'warning')
                : UI.badge('In stock', 'success');

        const row = document.createElement('tr');
        row.append(
            UI.cell(item.product_name),
            UI.cell(item.category, 'col-muted'),
            UI.cell(item.size || '—'),
            UI.cell(item.color || '—'),
            UI.cell(UI.formatPeso(item.price)),
            UI.cell(String(item.stock_quantity)),
            UI.cell(String(threshold), 'col-muted'),
            UI.cell(status)
        );
        return row;
    }), { fade });
}

document.getElementById('inventorySearch')?.addEventListener('input', UI.debounce(() => renderInventoryTable(), 200));

/* =========================
   PAGE LOAD
========================= */
document.addEventListener('DOMContentLoaded', loadInventory);
