// Fills the receipt with the sale the POS just saved, then opens the print dialog.
// The POS leaves the sale in localStorage; reading it clears it, so it doesn't stay on the device.
function takePendingSale() {
    const saved = localStorage.getItem('crkdPendingReceipt');
    localStorage.removeItem('crkdPendingReceipt');
    return saved ? JSON.parse(saved) : null;
}

function setText(id, text) {
    document.getElementById(id).textContent = text;
}

// Payment rows hold a label and a value; rows that don't apply to this sale stay hidden.
function showRow(id, value) {
    const row = document.getElementById(id);
    row.lastElementChild.textContent = value;
    row.hidden = false;
}

function itemRow(item) {
    const template = document.getElementById('receiptItemTemplate');
    const row = template.content.firstElementChild.cloneNode(true);
    row.querySelector('.item-name').textContent = item.name;
    row.querySelector('.item-quantity').textContent = `${item.quantity} x ${UI.formatPeso(item.price)}`;
    row.querySelector('.item-subtotal').textContent = UI.formatPeso(item.quantity * item.price);
    return row;
}

function renderReceipt(sale) {
    setText('receiptNumber', sale.referenceId);
    setText('receiptDate', new Date(sale.date_time).toLocaleString('en-PH'));
    setText('receiptCashier', sale.assistedBy);
    setText('receiptTotal', UI.formatPeso(sale.total_amount));
    document.getElementById('receiptItems').replaceChildren(...sale.items.map(itemRow));

    // A coupon or a manual discount shows the subtotal and what came off it ("Discount (SALE10, 10% off)"
    // or "Discount (Manual, 15% off)").
    if (sale.discount_amount > 0) {
        showRow('receiptSubtotal', UI.formatPeso(sale.subtotal_amount));
        showRow('receiptDiscount', `-${UI.formatPeso(sale.discount_amount)}`);
        document.getElementById('receiptDiscount').firstElementChild.textContent =
            `Discount (${sale.discountName || sale.discountCode}${sale.discountLabel ? `, ${sale.discountLabel}` : ''})`;
    }

    if (sale.payment_method === 'Cash') {
        showRow('receiptCash', UI.formatPeso(sale.amount_received));
        showRow('receiptChange', UI.formatPeso(sale.change_given));
    } else {
        showRow('receiptPayment', sale.payment_method || 'GCash');
        if (sale.referenceId) showRow('receiptReference', sale.referenceId);
    }

    document.getElementById('receipt').hidden = false;
}

const sale = takePendingSale();

if (sale) {
    renderReceipt(sale);
    window.addEventListener('load', () => window.print());
} else {
    document.getElementById('receiptEmpty').hidden = false;
}
