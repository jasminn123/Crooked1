/* =========================
   SALES HISTORY
========================= */
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
            cell.colSpan = 4;
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
                transaction.has_exchange ? 'Exchanged' : transaction.status
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
        cell.colSpan = 4;
        cell.className = 'transaction-empty';
        cell.textContent = 'Unable to load sales history.';
        row.appendChild(cell);
        tbody.appendChild(row);
    }
}

/* =========================
   PAGE LOAD
========================= */
document.addEventListener('DOMContentLoaded', loadSalesHistory);
