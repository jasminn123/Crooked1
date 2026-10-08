let cart = {};

function showDialog(title, message, onConfirm, onCancel, confirmText = 'OK', cancelText = 'Cancel') {
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
    okBtn.textContent = onConfirm ? confirmText : 'Close';
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
    cancelBtn.textContent = cancelText;
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

function showModal(id) {
  document.getElementById(id).classList.add("active");
}

function hideModal(id) {
  document.getElementById(id).classList.remove("active");
}

function confirmCancelCheckout() {
  showDialog(
    "Cancel Checkout",
    "Are you sure you want to cancel?",
    clearCheckout,
    undefined,
    "Yes, cancel",
    "Keep checkout"
  );
}

document.addEventListener("DOMContentLoaded", () => {
  const grid = document.getElementById("productGrid");
  const checkoutList = document.getElementById("checkoutList");
  const checkoutTotal = document.getElementById("checkoutTotal");
  const checkoutBtn = document.querySelector(".btn.checkout");
  // After loadProducts();
  const name = localStorage.getItem('userName');
  const profileCardName = document.querySelector('.profile-card-header span') || document.querySelector('#profileCard span');
  if (profileCardName && name) profileCardName.innerText = name;

  
  async function loadProducts() {
    try {
      const res = await fetch("http://localhost:5055/api/POS/products");
      const products = await res.json();

      grid.innerHTML = "";
      products.forEach(product => {
        const card = document.createElement("div");
        card.className = "product-card";
        card.innerHTML = `
          <img src="${product.imageUrl}" alt="${product.product_Name}">
          <h3>${product.product_Name}</h3>
          <p>₱${product.price}</p>
          <small>Stock: ${product.stock_Quantity}</small>
        `;
        card.addEventListener("click", () => addToCart(product));
        grid.appendChild(card);
      });
    } catch (err) {
      console.error("Error loading products:", err);
    }
  }

  function addToCart(product) {
    const id = product.id || product.product_Id;

    if (product.stock_Quantity <= 0) {
      showToast("Out of stock!", true);
      return;
    }

    if (cart[id]) {
      if (cart[id].qty < product.stock_Quantity) {
        cart[id].qty++;
      } else {
        showToast("No more stock available for " + product.product_Name, true);
        return;
      }
    } else {
      cart[id] = {
        id: id,
        name: product.product_Name,
        price: product.price,
        qty: 1,
        stock: product.stock_Quantity
      };
    }

    renderCheckout();
  }

  function changeCartQuantity(id, change) {
    const item = cart[id];
    if (!item) return;

    const newQuantity = item.qty + change;
    if (newQuantity > item.stock) {
      showToast("No more stock available for " + item.name, true);
      return;
    }

    if (newQuantity <= 0) {
      delete cart[id];
    } else {
      item.qty = newQuantity;
    }

    renderCheckout();
  }

  function renderCheckout() {
    checkoutList.innerHTML = "";
    let total = 0;

    Object.values(cart).forEach(item => {
      const subtotal = item.qty * item.price;
      total += subtotal;

      const li = document.createElement("li");
      li.className = "checkout-item";

      const details = document.createElement("div");
      details.className = "checkout-item-details";
      const name = document.createElement("span");
      name.className = "checkout-item-name";
      name.textContent = item.name;
      const itemSubtotal = document.createElement("span");
      itemSubtotal.className = "checkout-item-subtotal";
      itemSubtotal.textContent = `₱${subtotal.toLocaleString()}`;
      details.append(name, itemSubtotal);

      const quantityControls = document.createElement("div");
      quantityControls.className = "quantity-controls";

      const minusButton = document.createElement("button");
      minusButton.type = "button";
      minusButton.className = "quantity-button";
      minusButton.textContent = "−";
      minusButton.setAttribute("aria-label", `Decrease ${item.name} quantity`);
      minusButton.addEventListener("click", () => changeCartQuantity(item.id, -1));

      const quantity = document.createElement("span");
      quantity.className = "quantity-value";
      quantity.textContent = item.qty;
      quantity.setAttribute("aria-label", "Quantity");

      const plusButton = document.createElement("button");
      plusButton.type = "button";
      plusButton.className = "quantity-button";
      plusButton.textContent = "+";
      plusButton.setAttribute("aria-label", `Increase ${item.name} quantity`);
      plusButton.disabled = item.qty >= item.stock;
      plusButton.addEventListener("click", () => changeCartQuantity(item.id, 1));

      quantityControls.append(minusButton, quantity, plusButton);
      li.append(details, quantityControls);
      checkoutList.appendChild(li);
    });

    checkoutTotal.textContent = `₱${total.toLocaleString()}`;
  }

  function generateReferenceId() {
    return 'REF-' + Date.now() + '-' + Math.floor(Math.random() * 1000);
  }

  function buildTransaction(method, received = null, change = null, refId = null) {
    return {
      referenceId: refId || generateReferenceId(),
      date_time: new Date().toISOString(),
      total_amount: Object.values(cart).reduce((sum, item) => sum + item.price * item.qty, 0),
      status: "Completed",
      assistedBy: localStorage.getItem("userName")?.trim() || "Unknown",
      items: Object.values(cart).map(item => ({
        productId: item.id,
        name: item.name,
        quantity: item.qty,
        price: item.price
      })),
      payment_method: method,
      amount_received: received,
      change_given: change
    };
  }

  async function processCheckout(tx) {
    const cartPayload = Object.values(cart).map(item => ({
      productId: item.id,
      quantity: item.qty
    }));

    try {
      const checkoutRes = await fetch("http://localhost:5055/api/POS/checkout", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(cartPayload)
      });

      if (!checkoutRes.ok) throw new Error("Stock deduction failed");

      const transactionRes = await fetch("http://localhost:5055/api/POS/Transaction", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(tx)
      });
      if (!transactionRes.ok) throw new Error("Transaction could not be saved");

      console.log("Checkout and transaction saved.");
      return true;
    } catch (err) {
      console.error("Error during checkout:", err);
      showToast("Checkout failed. Please try again.", true);
      return false;
    }
  }

  function printReceipt(tx) {
    const receiptWindow = window.open("", "Receipt", "width=420,height=700");
    if (!receiptWindow) {
      showToast("The receipt window was blocked. Allow pop-ups and try printing again.", true);
      return;
    }

    const escapeHtml = value => String(value).replace(/[&<>"']/g, character => ({
      "&": "&amp;",
      "<": "&lt;",
      ">": "&gt;",
      '"': "&quot;",
      "'": "&#39;"
    })[character]);

    const formatPeso = amount => `₱${Number(amount || 0).toLocaleString("en-PH", {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2
    })}`;
    const items = tx.items.map(item => `
      <div class="item">
        <div class="item-name">${escapeHtml(item.name)}</div>
        <div class="item-details">
          <span>${item.quantity} x ${formatPeso(item.price)}</span>
          <span>${formatPeso(item.quantity * item.price)}</span>
        </div>
      </div>
    `).join("");
    const paymentDetails = tx.payment_method === "Cash" ? `
      <div class="row"><span>Cash</span><span>${formatPeso(tx.amount_received)}</span></div>
      <div class="row"><span>Change</span><span>${formatPeso(tx.change_given)}</span></div>
    ` : `
      <div class="row"><span>Payment</span><span>${escapeHtml(tx.payment_method || "GCash")}</span></div>
      ${tx.referenceId ? `<div class="row"><span>Reference</span><span>${escapeHtml(tx.referenceId)}</span></div>` : ""}
    `;

    receiptWindow.document.write(`<!DOCTYPE html>
      <html lang="en">
        <head>
          <meta charset="UTF-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Crooked Receipt</title>
          <style>
            * { box-sizing: border-box; }
            body {
              width: 72mm;
              margin: 0 auto;
              padding: 5mm 4mm;
              background: #fff;
              color: #000;
              font: 12px/1.4 "Courier New", monospace;
            }
            .receipt { width: 100%; }
            .center { text-align: center; }
            .shop-name {
              margin: 0;
              font-size: 23px;
              font-weight: 900;
              letter-spacing: 2px;
              line-height: 1.1;
            }
            .address { margin: 5px 0 12px; font-size: 11px; }
            .divider { border: 0; border-top: 1px dashed #000; margin: 9px 0; }
            .row, .item-details {
              display: flex;
              justify-content: space-between;
              gap: 8px;
            }
            .meta { font-size: 11px; }
            .meta div { margin: 2px 0; }
            .item { margin: 8px 0; }
            .item-name { overflow-wrap: anywhere; }
            .item-details { margin-top: 2px; padding-left: 8px; font-size: 11px; }
            .total { font-size: 15px; font-weight: bold; }
            .footer { margin-top: 14px; }
            @media print {
              @page { size: 80mm auto; margin: 0; }
              body { width: 72mm; padding: 4mm; }
            }
          </style>
        </head>
        <body>
          <main class="receipt">
            <header class="center">
              <h1 class="shop-name">CROOKED</h1>
              <p class="address">2057 Bagong Sikat St., Baclaran,<br>Parañaque City</p>
            </header>
            <hr class="divider">
            <section class="meta">
              <div>Receipt: ${escapeHtml(tx.referenceId)}</div>
              <div>Date: ${escapeHtml(new Date(tx.date_time).toLocaleString("en-PH"))}</div>
              <div>Cashier: ${escapeHtml(tx.assistedBy)}</div>
            </section>
            <hr class="divider">
            <section aria-label="Purchased items">${items}</section>
            <hr class="divider">
            <div class="row total"><span>TOTAL</span><span>${formatPeso(tx.total_amount)}</span></div>
            <hr class="divider">
            <section class="meta">${paymentDetails}</section>
            <hr class="divider">
            <footer class="center footer">
              <div>Thank you for shopping at</div>
              <strong>CROOKED</strong>
              <div>Please come again!</div>
            </footer>
          </main>
        </body>
      </html>
    `);
    receiptWindow.document.close();
    receiptWindow.focus();
    receiptWindow.print();
  }

  function askReceipt(tx) {
    showModal("receiptModal");

    document.getElementById("receiptYesBtn").onclick = () => {
      hideModal("receiptModal");
      printReceipt(tx);
      clearCheckout();
    };

    document.getElementById("receiptNoBtn").onclick = () => {
      hideModal("receiptModal");
      clearCheckout();
    };
  }

  checkoutBtn.addEventListener("click", () => {
    if (Object.keys(cart).length === 0) {
      showDialog("Notice", "Cart is empty.");
      return;
    }
    const total = Object.values(cart).reduce((sum, item) => sum + item.price * item.qty, 0);
    document.getElementById("modalTotal").textContent = `₱${total.toLocaleString()}`;
    showModal("paymentModal");
  });

  document.getElementById("cashBtn").addEventListener("click", () => {
    hideModal("paymentModal");
    showModal("cashModal");
    document.getElementById("cashTotal").textContent = checkoutTotal.textContent;
  });

  document.getElementById("gcashBtn").addEventListener("click", () => {
    hideModal("paymentModal");
    showModal("gcashModal");
    document.getElementById("gcashTotal").textContent = checkoutTotal.textContent;
  });

  const amountReceivedInput = document.getElementById("amountReceived");
  amountReceivedInput.addEventListener("input", () => {
    const parts = amountReceivedInput.value.replace(/[^\d.]/g, '').split('.');
    const wholeAmount = parts.shift().slice(0, 5);
    const decimalAmount = parts.join('').slice(0, 2);
    amountReceivedInput.value = parts.length || amountReceivedInput.value.includes('.')
      ? `${wholeAmount}.${decimalAmount}`
      : wholeAmount;

    const received = parseFloat(amountReceivedInput.value);
    const total = parseFloat(checkoutTotal.textContent.replace(/[₱,]/g, ""));
    if (!isNaN(received) && received >= 0) {
      const change = received - total;
      document.getElementById("changeDisplay").textContent = `₱${change.toLocaleString()}`;
    } else {
      document.getElementById("changeDisplay").textContent = "₱0.00";
    }
  });

  document.getElementById("confirmCashBtn").addEventListener("click", async () => {
    const received = parseFloat(document.getElementById("amountReceived").value);
    const total = parseFloat(checkoutTotal.textContent.replace(/[₱,]/g, ""));

    if (isNaN(received) || received < total) {
      showDialog("Payment Not Accepted", "The amount received is less than the total.");
      return;
    }

    const change = received - total;
    const tx = buildTransaction("Cash", received, change);

    if (!await processCheckout(tx)) return;
    hideModal("cashModal");
    askReceipt(tx);
  });

  document.getElementById("confirmGCashBtn").addEventListener("click", async () => {
    const refId = document.getElementById("gcashRefId").value.trim();

    if (!refId) {
      showToast("Please enter a reference ID.", true);
      return;
    }

    const tx = buildTransaction("GCash", null, null, refId);

    if (!await processCheckout(tx)) return;
    hideModal("gcashModal");
    askReceipt(tx);
  });

  document.getElementById("closeReceiptBtn")?.addEventListener("click", () => hideModal("receiptModal"));
  document.getElementById("closePaymentBtn")?.addEventListener("click", () => hideModal("paymentModal"));

  const userIcon = document.getElementById("userIcon");
  const profileCard = document.getElementById("profileCard");
  const profileSection = document.querySelector(".profile-section");

  userIcon.addEventListener("click", (event) => {
    event.stopPropagation();
    profileCard.style.display = (profileCard.style.display === "flex") ? "none" : "flex";
  });

  document.addEventListener("click", (event) => {
    if (!profileSection.contains(event.target)) {
      profileCard.style.display = "none";
    }
  });

  loadProducts();
});

function clearCheckout() {
  const checkoutList = document.getElementById("checkoutList");
  const checkoutTotal = document.getElementById("checkoutTotal");

  checkoutList.innerHTML = "";
  checkoutTotal.textContent = "₱0.00";
  cart = {};

  hideModal("paymentModal");
  hideModal("cashModal");
  hideModal("gcashModal");
}