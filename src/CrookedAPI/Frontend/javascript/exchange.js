/* =========================
   EXCHANGES
   Shop policy: an item bought as a single piece can be exchanged within
   3 days of purchase for the same product in a different size and/or
   color at the same price. The returned item goes back into stock and
   the replacement is taken out of stock; the sale is then shown as
   Exchanged. The server enforces all of this; the page only guides.
========================= */
const RECENT_SALES_PAGE_SIZE = 5;
const RECENT_SALES_COLUMNS = 6;
const SIZE_ORDER = ['XXS', 'XS', 'S', 'M', 'L', 'XL', 'XXL', '2XL', 'XXXL', '3XL', '4XL', '5XL'];

// The sale being exchanged: { transaction, exchangeUntil, hiddenItemCount, items, exchanges }
// from /api/Exchange/lookup.
let sale = null;
// transactionItemId -> { productId, size } for every item ticked for exchange.
const selections = new Map();
let exchangePending = false;
let recentSalesPage = 1;
let focusRecentPager = false;
let staleNoticeShown = false;

const lookupForm = document.getElementById('lookupForm');
const lookupQuery = document.getElementById('lookupQuery');
const lookupButton = document.getElementById('lookupButton');
const lookupStatus = document.getElementById('lookupStatus');
const workspace = document.getElementById('exchangeWorkspace');
const itemsContainer = document.getElementById('exchangeItems');
const notesInput = document.getElementById('exchangeNotes');
const errorBox = document.getElementById('exchangeError');
const submitButton = document.getElementById('submitExchange');
const resultSection = document.getElementById('exchangeResult');

function scrollToSection(section) {
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    section.scrollIntoView({ block: 'start', behavior: reduceMotion ? 'auto' : 'smooth' });
}

function sizeRank(size) {
    const index = SIZE_ORDER.indexOf(size.toUpperCase());
    return index === -1 ? SIZE_ORDER.length : index;
}

function sameText(a, b) {
    return (a || '').trim().toLowerCase() === (b || '').trim().toLowerCase();
}

function saleLabel(transaction) {
    return transaction.referenceId || `#${transaction.transactionId}`;
}

/* =========================
   STEP 1: FIND THE SALE
========================= */
function setLookupStatus(text, isError = false) {
    lookupStatus.textContent = text;
    lookupStatus.classList.toggle('is-error', isError);
}

async function findSale(query) {
    const text = query.trim();
    if (!text) {
        setLookupStatus('Enter a reference ID or transaction number.', true);
        lookupQuery.setAttribute('aria-invalid', 'true');
        lookupQuery.focus();
        return;
    }

    lookupQuery.removeAttribute('aria-invalid');
    const restore = UI.setLoading(lookupButton, 'Finding...');
    setLookupStatus('Looking up the sale...');
    try {
        const data = await UI.fetchJson(`${apiBase}/api/Exchange/lookup?query=${encodeURIComponent(text)}`);
        setLookupStatus('');
        openWorkspace(data);
    } catch (error) {
        console.error('Exchange Lookup Error:', error);
        setLookupStatus(error.message || 'The sale could not be loaded. Please try again.', true);
        lookupQuery.focus();
    } finally {
        restore();
    }
}

// Only sales that can still be exchanged: completed, from the last 3 days, with single-piece items.
async function loadRecentSales(page = recentSalesPage, { quiet = false } = {}) {
    const body = document.getElementById('recentSalesBody');
    const pager = document.getElementById('recentSalesPagination');
    recentSalesPage = page;
    if (!quiet) UI.tableSkeleton(body, RECENT_SALES_COLUMNS, RECENT_SALES_PAGE_SIZE);

    try {
        const data = await UI.fetchJson(
            `${apiBase}/api/Exchange/sales?page=${page}&pageSize=${RECENT_SALES_PAGE_SIZE}`);
        if (!data.items.length) {
            pager.replaceChildren();
            UI.tableMessage(body, RECENT_SALES_COLUMNS, {
                icon: 'fa-receipt',
                title: 'No sales to exchange',
                message: `Sales from the last ${data.windowDays} days with items bought as a single piece appear here.`
            });
            return;
        }

        UI.fillTable(body, data.items.map(transaction => {
            const select = UI.el('button', 'btn btn-secondary btn-sm', 'Select');
            select.type = 'button';
            select.setAttribute('aria-label', `Select sale ${saleLabel(transaction)}`);
            select.addEventListener('click', () => {
                lookupQuery.value = transaction.referenceId || String(transaction.transactionId);
                findSale(lookupQuery.value);
            });

            const row = document.createElement('tr');
            row.append(
                UI.cell(transaction.dateTime, 'col-muted'),
                UI.cell(transaction.referenceId || '—'),
                UI.cell(UI.formatPeso(transaction.totalAmount), 'col-amount'),
                UI.cell(transaction.exchangeUntil, 'col-muted'),
                UI.cell(UI.statusBadge(transaction.status)),
                UI.cell(select, 'col-action')
            );
            return row;
        }), { fade: !quiet });

        UI.pagination(pager, {
            page: data.page,
            totalPages: data.totalPages,
            totalItems: data.totalItems,
            pageSize: data.pageSize,
            itemLabel: 'sales',
            onChange: nextPage => {
                focusRecentPager = true;
                loadRecentSales(nextPage);
            }
        });
        if (focusRecentPager) {
            pager.querySelector('[aria-current="page"]')?.focus();
            focusRecentPager = false;
        }
    } catch (error) {
        console.error('Recent Sales Error:', error);
        pager.replaceChildren();
        UI.tableMessage(body, RECENT_SALES_COLUMNS, {
            isError: true,
            title: 'Unable to load sales',
            message: error.message,
            actionLabel: 'Try again',
            onAction: () => loadRecentSales(page)
        });
    }
}

/* =========================
   STEP 2: CHOOSE WHAT TO EXCHANGE
========================= */
function openWorkspace(data) {
    sale = data;
    selections.clear();
    staleNoticeShown = false;
    resultSection.hidden = true;
    notesInput.value = '';
    hideError();

    const transaction = data.transaction;
    document.getElementById('saleSummary').replaceChildren(
        UI.detailPair('Reference ID', saleLabel(transaction)),
        UI.detailPair('Date & time', transaction.dateTime),
        UI.detailPair('Exchange until', data.exchangeUntil),
        UI.detailPair('Payment method', transaction.paymentMethod),
        UI.detailPair('Sold by', transaction.soldBy || 'Unknown'),
        UI.detailPair('Status', UI.statusBadge(transaction.status)),
        UI.detailPair('Total amount', UI.formatPeso(transaction.totalAmount))
    );
    itemsContainer.replaceChildren(...data.items.map(buildItemCard));

    const hiddenNote = document.getElementById('hiddenItemsNote');
    hiddenNote.hidden = !data.hiddenItemCount;
    hiddenNote.textContent = data.hiddenItemCount
        ? `${data.hiddenItemCount} item${data.hiddenItemCount === 1 ? ' was' : 's were'} bought in a quantity of 2 or more and can't be exchanged, so ${data.hiddenItemCount === 1 ? 'it isn\'t' : 'they aren\'t'} listed.`
        : '';
    updateReview();

    workspace.hidden = false;
    workspace.classList.remove('fade-in');
    void workspace.offsetWidth;
    workspace.classList.add('fade-in');
    history.replaceState(null, '', `?sale=${encodeURIComponent(transaction.referenceId || transaction.transactionId)}`);
    scrollToSection(workspace);
    document.getElementById('workspaceTitle').focus({ preventScroll: true });
}

function selectField(id, labelText, context) {
    const field = UI.el('div', 'form-field');
    const label = UI.el('label', null, labelText);
    label.htmlFor = id;
    label.appendChild(UI.el('span', 'visually-hidden', ` for ${context}`));
    const select = UI.el('select');
    select.id = id;
    field.append(label, select);
    return { field, select };
}

function buildItemCard(item) {
    const id = item.transactionItemId;
    const bought = UI.variantLabel(item.size, item.color);
    const card = UI.el('article', 'exchange-item');

    const info = UI.el('div', 'exchange-item-info');
    info.append(
        UI.el('h3', 'exchange-item-name', item.productName),
        UI.el('p', 'exchange-item-meta', `Bought: ${bought} · ${UI.formatPeso(item.unitPrice)}`)
    );
    if (item.exchangeReason) info.appendChild(UI.badge(`Exchanged before: ${item.exchangeReason}`, 'info'));
    card.appendChild(info);

    if (!item.options.length) {
        card.appendChild(UI.el('p', 'exchange-item-note',
            'No other size or color of this product is available at the same price, so it can\'t be exchanged.'));
        return card;
    }

    const context = `${item.productName}, ${bought}`;
    const toggle = UI.el('label', 'exchange-item-toggle');
    const checkbox = UI.el('input');
    checkbox.type = 'checkbox';
    checkbox.id = `exchange-item-${id}`;
    toggle.append(checkbox, 'Exchange this item', UI.el('span', 'visually-hidden', `: ${context}`));

    const size = selectField(`exchange-size-${id}`, 'New size', context);
    const color = selectField(`exchange-color-${id}`, 'New color', context);
    const availability = UI.el('p', 'exchange-item-availability');
    availability.id = `exchange-availability-${id}`;
    availability.dataset.itemId = id;
    [size.select, color.select].forEach(select => select.setAttribute('aria-describedby', availability.id));

    const sizes = [...new Set(item.options.map(option => option.size?.trim() || 'One Size'))]
        .sort((a, b) => sizeRank(a) - sizeRank(b) || a.localeCompare(b, undefined, { numeric: true }));
    const sizeInStock = value => item.options.some(option =>
        (option.size?.trim() || 'One Size') === value && option.stock > 0);
    size.select.append(...sizes.map(value => {
        let label = sameText(value, item.size || 'One Size') ? `${value} (same as bought)` : value;
        if (!sizeInStock(value)) label += ' — out of stock';
        const element = new Option(label, value);
        element.disabled = !sizeInStock(value);
        return element;
    }));
    size.select.value = sizes.find(sizeInStock) || sizes[0];

    function renderColors() {
        const variants = item.options.filter(option => (option.size?.trim() || 'One Size') === size.select.value);
        color.select.replaceChildren(...variants.map(option => {
            const label = `${option.color?.trim() || 'Default'} — ${option.stock > 0 ? `${option.stock} in stock` : 'out of stock'}`;
            const element = new Option(label, String(option.productId));
            element.disabled = option.stock <= 0;
            return element;
        }));
        const firstInStock = variants.find(option => option.stock > 0);
        if (firstInStock) color.select.value = String(firstInStock.productId);
    }

    function sync() {
        const chosen = checkbox.checked;
        size.select.disabled = !chosen;
        color.select.disabled = !chosen;
        card.classList.toggle('is-selected', chosen);
        if (chosen) selections.set(id, { productId: Number(color.select.value) || 0, size: size.select.value });
        else selections.delete(id);
        updateReview();
    }

    renderColors();
    checkbox.addEventListener('change', sync);
    size.select.addEventListener('change', () => {
        renderColors();
        sync();
    });
    color.select.addEventListener('change', sync);
    sync();

    const controls = UI.el('div', 'exchange-item-controls');
    controls.append(size.field, color.field);
    card.append(toggle, controls, availability);
    return card;
}

// Shows the stock moves and blocks the exchange when a replacement is out of stock.
function updateReview() {
    if (!sale) return;

    const itemsById = new Map(sale.items.map(item => [item.transactionItemId, item]));
    const optionsById = new Map();
    sale.items.forEach(item => item.options.forEach(option => optionsById.set(option.productId, option)));

    // Net stock change per product, as the server applies it: returns add, replacements subtract.
    const stockChange = new Map();
    selections.forEach((selection, itemId) => {
        const item = itemsById.get(itemId);
        stockChange.set(item.productId, (stockChange.get(item.productId) || 0) + 1);
        stockChange.set(selection.productId, (stockChange.get(selection.productId) || 0) - 1);
    });

    let hasProblem = false;
    itemsContainer.querySelectorAll('.exchange-item-availability').forEach(element => {
        const selection = selections.get(Number(element.dataset.itemId));
        element.classList.remove('is-error');
        if (!selection) {
            element.textContent = 'Not being exchanged.';
            return;
        }

        const option = optionsById.get(selection.productId);
        if (!option || option.stock <= 0) {
            hasProblem = true;
            element.classList.add('is-error');
            element.textContent = `Size ${selection.size} is out of stock in every color. Ask the customer to pick another size or color.`;
            return;
        }

        const left = option.stock + (stockChange.get(option.productId) || 0);
        if (left < 0) {
            hasProblem = true;
            element.classList.add('is-error');
            element.textContent = `Only ${option.stock} of ${UI.variantLabel(option.size, option.color)} in stock.`;
        } else {
            element.textContent = `Will be swapped for ${UI.variantLabel(option.size, option.color)}. ${left} left after the exchange.`;
        }
    });

    const returnList = document.getElementById('returnList');
    const issueList = document.getElementById('issueList');
    returnList.replaceChildren();
    issueList.replaceChildren();
    selections.forEach((selection, itemId) => {
        const item = itemsById.get(itemId);
        const option = optionsById.get(selection.productId);
        returnList.appendChild(UI.el('li', null, `${item.productName} — ${UI.variantLabel(item.size, item.color)}`));
        if (option) {
            issueList.appendChild(UI.el('li', null, `${item.productName} — ${UI.variantLabel(option.size, option.color)}`));
        }
    });

    document.getElementById('reviewEmpty').hidden = selections.size > 0;
    document.getElementById('reviewColumns').hidden = selections.size === 0;
    submitButton.disabled = exchangePending || selections.size === 0 || hasProblem;
}

function showError(message, withReload = false) {
    errorBox.replaceChildren(UI.icon('fa-triangle-exclamation'), ` ${message}`);
    if (withReload) {
        const reload = UI.el('button', 'link-button', 'Reload the sale');
        reload.type = 'button';
        reload.addEventListener('click', reloadSale);
        errorBox.append(' ', reload);
    }
    errorBox.hidden = false;
}

function hideError() {
    errorBox.hidden = true;
    errorBox.replaceChildren();
}

function reloadSale() {
    if (sale) findSale(String(sale.transaction.referenceId || sale.transaction.transactionId));
}

function confirmExchange() {
    if (!sale || !selections.size) return;

    const count = selections.size;
    showDialog(
        'Process Exchange',
        `Exchange ${count} item${count === 1 ? '' : 's'} on sale ${saleLabel(sale.transaction)}? ` +
        'The returned items go back into stock, the replacements are taken out of stock, and the sale will be marked Exchanged.',
        submitExchange,
        null,
        { confirmText: 'Process Exchange' }
    );
}

async function submitExchange() {
    if (exchangePending || !sale) return;

    exchangePending = true;
    hideError();
    const restore = UI.setLoading(submitButton, 'Processing...');
    try {
        const result = await UI.fetchJson(`${apiBase}/api/Exchange`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                transactionId: sale.transaction.transactionId,
                processedBy: localStorage.getItem('userName')?.trim() || 'Unknown',
                notes: notesInput.value.trim(),
                items: [...selections].map(([transactionItemId, selection]) => ({
                    transactionItemId,
                    quantity: 1,
                    newProductId: selection.productId
                }))
            })
        });

        restore();
        showResult(result);
        UI.toast(`Exchange saved. Sale ${result.referenceId} is now marked Exchanged.`, { tone: 'success' });
        loadRecentExchanges();
        loadRecentSales();
    } catch (error) {
        console.error('Exchange Error:', error);
        restore();
        showError(error.message || 'The exchange could not be saved. Please try again.', error.status === 409);
    } finally {
        exchangePending = false;
        updateReview();
    }
}

function showResult(result) {
    const describe = (entry, suffix) =>
        UI.el('li', null, `${entry.productName} — ${UI.variantLabel(entry.size, entry.color)} · ${suffix(entry.stockAfter)}`);
    document.getElementById('resultText').textContent =
        `Sale ${result.referenceId} is now marked Exchanged, and the stock levels below were updated.`;
    document.getElementById('resultReturned').replaceChildren(
        ...result.returned.map(entry => describe(entry, stock => `${stock} now in stock`)));
    document.getElementById('resultIssued').replaceChildren(
        ...result.issued.map(entry => describe(entry, stock => `${stock} left`)));

    sale = null;
    selections.clear();
    workspace.hidden = true;
    lookupQuery.value = '';
    history.replaceState(null, '', window.location.pathname);

    resultSection.hidden = false;
    resultSection.classList.remove('fade-in');
    void resultSection.offsetWidth;
    resultSection.classList.add('fade-in');
    scrollToSection(resultSection);
    document.getElementById('resultTitle').focus({ preventScroll: true });
}

function leaveWorkspace() {
    const close = () => {
        sale = null;
        selections.clear();
        workspace.hidden = true;
        history.replaceState(null, '', window.location.pathname);
        scrollToSection(document.getElementById('findSale'));
        lookupQuery.focus({ preventScroll: true });
        lookupQuery.select();
    };

    if (!selections.size && !notesInput.value.trim()) {
        close();
        return;
    }
    showDialog(
        'Discard Exchange?',
        'The items you chose for this exchange will be cleared.',
        close,
        null,
        { confirmText: 'Discard', danger: true }
    );
}

// Someone else changed stock while this sale is open. Rebuilding the cards would throw away
// the cashier's choices, so they are told once and can reload when ready.
function warnStaleStock() {
    if (!sale || exchangePending || staleNoticeShown) return;
    staleNoticeShown = true;
    showError('Stock levels just changed on another device.', true);
}

/* =========================
   RECENT EXCHANGES
========================= */
async function loadRecentExchanges({ quiet = false } = {}) {
    const body = document.getElementById('recentExchangesBody');
    if (!quiet) UI.tableSkeleton(body, 4, 4);

    try {
        const exchanges = await UI.fetchJson(`${apiBase}/api/Exchange/recent?limit=10`);
        if (!exchanges.length) {
            UI.tableMessage(body, 4, {
                icon: 'fa-right-left',
                title: 'No exchanges yet',
                message: 'Exchanges you process will be listed here.'
            });
            return;
        }

        UI.fillTable(body, exchanges.map(exchange => {
            const changes = UI.el('ul', 'exchange-change-list');
            exchange.items.forEach(item => changes.appendChild(UI.el('li', null,
                `${item.productName}: ${UI.variantLabel(item.returnedSize, item.returnedColor)} → ` +
                `${UI.variantLabel(item.newSize, item.newColor)}`)));

            const row = document.createElement('tr');
            row.append(
                UI.cell(exchange.createdAt, 'col-muted'),
                UI.cell(exchange.referenceId || `#${exchange.transactionId}`),
                UI.cell(changes, 'col-wrap'),
                UI.cell(exchange.processedBy, 'col-muted')
            );
            return row;
        }), { fade: !quiet });
    } catch (error) {
        console.error('Recent Exchanges Error:', error);
        UI.tableMessage(body, 4, {
            isError: true,
            title: 'Unable to load recent exchanges',
            message: error.message,
            actionLabel: 'Try again',
            onAction: () => loadRecentExchanges()
        });
    }
}

/* =========================
   PAGE LOAD
========================= */
document.addEventListener('DOMContentLoaded', () => {
    lookupForm.addEventListener('submit', event => {
        event.preventDefault();
        findSale(lookupQuery.value);
    });
    lookupQuery.addEventListener('input', () => {
        lookupQuery.removeAttribute('aria-invalid');
        if (lookupStatus.classList.contains('is-error')) setLookupStatus('');
    });
    submitButton.addEventListener('click', confirmExchange);
    document.getElementById('cancelExchange').addEventListener('click', leaveWorkspace);
    document.getElementById('changeSale').addEventListener('click', leaveWorkspace);
    document.getElementById('newExchange').addEventListener('click', () => {
        resultSection.hidden = true;
        scrollToSection(document.getElementById('findSale'));
        lookupQuery.focus({ preventScroll: true });
    });

    loadRecentSales(1);
    loadRecentExchanges();
    Live.on(['transactions', 'exchanges'], () => {
        loadRecentSales(recentSalesPage, { quiet: true });
        loadRecentExchanges({ quiet: true });
    });
    Live.on(['products'], warnStaleStock);

    const presetSale = new URLSearchParams(window.location.search).get('sale');
    if (presetSale) {
        lookupQuery.value = presetSale;
        findSale(presetSale);
    }
});
