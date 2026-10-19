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

    tbody.replaceChildren();
    if (!transactions || !transactions.length) {
        const row = document.createElement('tr');
        const cell = document.createElement('td');
        cell.colSpan = 6;
        cell.className = 'transaction-empty';
        cell.textContent = 'No transactions found.';
        row.appendChild(cell);
        tbody.appendChild(row);
        return;
    }

    transactions.forEach(txn => {
        const displayStatus = txn.has_exchange ? 'Exchanged' : txn.status;
        const statusClass = {
            'Completed': 'status-completed',
            'Exchanged': 'status-exchanged',
            'Pending': 'status-pending',
            'Voided': 'status-voided'
        }[displayStatus] || '';

        const row = document.createElement('tr');
        [
            txn.date_time,
            `₱${Number(txn.total_amount).toLocaleString('en-PH', { minimumFractionDigits: 2 })}`,
            txn.reference_id || '—',
            txn.assisted_by || 'Unknown',
            displayStatus
        ].forEach((value, index) => {
            const cell = document.createElement('td');
            cell.textContent = value;
            if (index === 0 || index === 2 || index === 3) cell.className = 'col-muted';
            if (index === 4) cell.className = statusClass;
            row.appendChild(cell);
        });

        const editCell = document.createElement('td');
        const editButton = document.createElement('button');
        editButton.type = 'button';
        editButton.className = 'transaction-edit-button';
        editButton.setAttribute('aria-label', `Edit items in transaction ${txn.transaction_id}`);
        editButton.title = 'Edit or exchange purchased items';
        editButton.innerHTML = '<i class="fa-solid fa-pen-to-square" aria-hidden="true"></i>';
        editButton.disabled = txn.status !== 'Completed';
        editButton.addEventListener('click', () => openTransactionExchange(txn));
        editCell.appendChild(editButton);
        row.appendChild(editCell);
        tbody.appendChild(row);
    });
}

let exchangeEditorItems = [];
let exchangeEditorVariants = [];
let exchangeTransaction = null;
// Set when the user changes a size, color or reason, so closing the editor can ask first.
let exchangeHasChanges = false;

function formatTransactionAmount(amount) {
    return `₱${Number(amount).toLocaleString('en-PH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

async function openTransactionExchange(transaction) {
    const modal = document.getElementById('exchangeTransactionModal');
    const body = document.getElementById('exchangeItemsBody');
    const status = document.getElementById('exchangeModalStatus');
    const saveButton = document.getElementById('saveExchangeButton');
    exchangeTransaction = transaction;
    exchangeEditorItems = [];
    exchangeEditorVariants = [];
    body.replaceChildren();
    status.textContent = 'Loading purchased items...';
    status.className = 'exchange-modal-status';
    saveButton.disabled = true;
    document.getElementById('exchangeModalTitle').textContent = `Edit purchased items · #${transaction.transaction_id}`;
    document.getElementById('exchangeUpdatedTotal').textContent = formatTransactionAmount(transaction.total_amount);
    exchangeHasChanges = false;
    openModalElement(modal, () => closeTransactionExchange());

    try {
        const response = await fetch(`${apiBase}/api/POS/Transaction/${encodeURIComponent(transaction.transaction_id)}/items`);
        const responseText = await response.text();
        let data = {};
        if (responseText) {
            try {
                data = JSON.parse(responseText);
            } catch {
                data = {};
            }
        }
        if (!response.ok) throw new Error(data.message || `Could not load transaction items (${response.status})`);

        exchangeEditorItems = data.items.flatMap(item =>
            Array.from({ length: item.quantity }, () => ({
                ...item,
                originalTransactionItemId: item.transactionItemId,
                originalProductId: item.productId,
                originalQuantity: item.quantity,
                productId: item.productId,
                quantity: 1,
                reason: item.exchangeReason || ''
            }))
        );
        exchangeEditorVariants = data.variants;
        renderExchangeItems();
        updateExchangeTotal();
        status.textContent = 'Each row represents one unit. Exchange reasons are optional.';
        saveButton.disabled = false;
    } catch (error) {
        console.error('Transaction Items Error:', error);
        status.textContent = error.message || 'Unable to load purchased items.';
        status.classList.add('error');
    }
}

function matchingExchangeVariants(item) {
    return exchangeEditorVariants.filter(variant =>
        variant.productName.toLowerCase() === item.productName.toLowerCase()
        && variant.category.toLowerCase() === item.category.toLowerCase()
        && Number(variant.price) === Number(item.unitPrice)
    );
}

function renderExchangeItems() {
    const body = document.getElementById('exchangeItemsBody');
    body.replaceChildren();

    exchangeEditorItems.forEach(item => {
        const row = document.createElement('tr');
        const productCell = document.createElement('td');
        productCell.textContent = item.productName;

        const variants = matchingExchangeVariants(item);
        const selectedVariant = variants.find(variant => variant.id === item.productId);
        if (!selectedVariant && item.productId) {
            variants.unshift({
                id: item.productId,
                productName: item.productName,
                category: item.category,
                size: item.size,
                color: item.color,
                price: item.unitPrice,
                stockQuantity: 0
            });
        }

        const sizeCell = document.createElement('td');
        const sizeSelect = document.createElement('select');
        sizeSelect.className = 'exchange-select';
        sizeSelect.setAttribute('aria-label', `Size for ${item.productName}`);
        sizeSelect.replaceChildren(...[...new Set(variants.map(variant => variant.size || 'One Size'))]
            .map(size => new Option(size, size)));

        const colorCell = document.createElement('td');
        const colorSelect = document.createElement('select');
        colorSelect.className = 'exchange-select';
        colorSelect.setAttribute('aria-label', `Color for ${item.productName}`);

        const quantityCell = document.createElement('td');
        const quantityLabel = document.createElement('span');
        quantityLabel.className = 'exchange-static-quantity';
        quantityLabel.textContent = '1×';

        const reasonCell = document.createElement('td');
        const reasonSelect = document.createElement('select');
        reasonSelect.className = 'exchange-select exchange-reason-select';
        reasonSelect.setAttribute('aria-label', `Exchange reason for ${item.productName}`);
        reasonSelect.replaceChildren(
            new Option('Select reason', ''),
            new Option('Change Color', 'Change Color'),
            new Option('Change Size', 'Change Size'),
            new Option('Damaged', 'Damaged')
        );
        reasonSelect.value = item.reason;
        reasonSelect.addEventListener('change', () => {
            item.reason = reasonSelect.value;
            exchangeHasChanges = true;
        });

        function renderColors(preferredColor) {
            const matchingSizeVariants = variants.filter(variant =>
                (variant.size || 'One Size') === sizeSelect.value);
            const colors = [...new Set(matchingSizeVariants.map(variant => variant.color || 'Default'))];
            colorSelect.replaceChildren(...colors.map(color => new Option(color, color)));
            colorSelect.value = colors.includes(preferredColor) ? preferredColor : colors[0];
            updateSelectedVariant();
        }

        function updateSelectedVariant() {
            const variant = variants.find(candidate =>
                (candidate.size || 'One Size') === sizeSelect.value
                && (candidate.color || 'Default') === colorSelect.value);
            item.productId = variant?.id || 0;
            updateExchangeTotal();
        }

        sizeSelect.value = item.size || 'One Size';
        renderColors(item.color || 'Default');
        sizeSelect.addEventListener('change', () => {
            exchangeHasChanges = true;
            renderColors();
        });
        colorSelect.addEventListener('change', () => {
            exchangeHasChanges = true;
            updateSelectedVariant();
        });
        sizeCell.appendChild(sizeSelect);
        colorCell.appendChild(colorSelect);
        quantityCell.appendChild(quantityLabel);
        reasonCell.appendChild(reasonSelect);
        row.append(productCell, sizeCell, colorCell, quantityCell, reasonCell);
        body.appendChild(row);
    });
}

function updateExchangeTotal() {
    const total = exchangeEditorItems.reduce((sum, item) =>
        sum + Number(item.unitPrice) * Math.max(1, Number(item.quantity) || 1), 0);
    document.getElementById('exchangeUpdatedTotal').textContent = formatTransactionAmount(total);
}

async function saveTransactionExchange() {
    if (!exchangeTransaction || !exchangeEditorItems.length) return;

    const status = document.getElementById('exchangeModalStatus');
    const items = exchangeEditorItems.map(item => ({
        transactionItemId: item.originalTransactionItemId,
        productId: item.productId,
        reason: item.reason
    }));
    if (items.some(item => item.productId <= 0)) {
        status.textContent = 'Select a valid size and color for every item.';
        status.className = 'exchange-modal-status error';
        return;
    }

    showDialog(
        'Save Exchange',
        `Save the changes to transaction #${exchangeTransaction.transaction_id}? Inventory will be updated for every item you exchanged.`,
        () => submitTransactionExchange(items),
        null,
        { confirmText: 'Save Exchange' }
    );
}

async function submitTransactionExchange(items) {
    if (!exchangeTransaction) return;

    const saveButton = document.getElementById('saveExchangeButton');
    const status = document.getElementById('exchangeModalStatus');
    saveButton.disabled = true;
    status.textContent = 'Saving exchange...';
    status.className = 'exchange-modal-status';
    try {
        const response = await fetch(
            `${apiBase}/api/POS/Transaction/${encodeURIComponent(exchangeTransaction.transaction_id)}/items`,
            {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ items })
            }
        );
        const responseText = await response.text();
        let result = {};
        if (responseText) {
            try {
                result = JSON.parse(responseText);
            } catch {
                result = {};
            }
        }
        if (!response.ok) throw new Error(result.message || `Could not save exchange (${response.status})`);

        closeTransactionExchange(true);
        await loadTransactions();
        showDialog('Exchange Saved', 'Purchased items and inventory have been updated.');
    } catch (error) {
        console.error('Transaction Exchange Error:', error);
        status.textContent = error.message || 'Unable to save exchange.';
        status.className = 'exchange-modal-status error';
        saveButton.disabled = false;
    }
}

function closeTransactionExchange(force = false) {
    const close = () => {
        closeModalElement(document.getElementById('exchangeTransactionModal'));
        exchangeEditorItems = [];
        exchangeEditorVariants = [];
        exchangeTransaction = null;
        exchangeHasChanges = false;
    };

    if (force || !exchangeHasChanges) {
        close();
        return;
    }

    showDialog(
        'Discard Changes?',
        'You changed items in this transaction. Close the editor and discard those changes?',
        close,
        null,
        { confirmText: 'Discard', danger: true }
    );
}

/* =========================
   PAGE LOAD
========================= */
document.addEventListener('DOMContentLoaded', () => {
    document.getElementById('closeExchangeModal')?.addEventListener('click', () => closeTransactionExchange());
    document.getElementById('cancelExchangeButton')?.addEventListener('click', () => closeTransactionExchange());
    document.getElementById('saveExchangeButton')?.addEventListener('click', saveTransactionExchange);
    document.getElementById('exchangeTransactionModal')?.addEventListener('click', event => {
        if (event.target.id === 'exchangeTransactionModal') closeTransactionExchange();
    });

    loadTransactions();
});
