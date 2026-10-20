/* =========================
   SALES HISTORY
========================= */
async function loadSalesHistory({ quiet = false } = {}) {
    const tbody = document.getElementById('salesHistoryTableBody');
    if (!tbody) return;
    if (!quiet) UI.tableSkeleton(tbody, 4, 8);

    try {
        const transactions = await UI.fetchJson(`${apiBase}/api/POS/Transaction`);
        if (!transactions.length) {
            UI.tableMessage(tbody, 4, {
                icon: 'fa-chart-line',
                title: 'No sales recorded yet',
                message: 'Sales made at the POS will appear here.'
            });
            return;
        }

        UI.fillTable(tbody, transactions.map(transaction => {
            const row = document.createElement('tr');
            row.append(
                UI.cell(transaction.date_time, 'col-muted'),
                UI.cell(transaction.reference_id || '—'),
                UI.cell(UI.formatPeso(transaction.total_amount)),
                UI.cell(UI.statusBadge(transaction.has_exchange ? 'Exchanged' : transaction.status))
            );
            return row;
        }), { fade: !quiet });
    } catch (error) {
        console.error('Sales History Error:', error);
        UI.tableMessage(tbody, 4, {
            isError: true,
            title: 'Unable to load sales history',
            message: error.message,
            actionLabel: 'Try again',
            onAction: () => loadSalesHistory()
        });
    }
}

/* =========================
   PAGE LOAD
========================= */
document.addEventListener('DOMContentLoaded', () => {
    loadSalesHistory();
    Live.on(['transactions', 'exchanges'], () => loadSalesHistory({ quiet: true }));
});
