let cart = {};

function showModal(id) {
  document.getElementById(id).classList.add("active");
}

function hideModal(id) {
  document.getElementById(id).classList.remove("active");
}

document.addEventListener("DOMContentLoaded", () => {
  const grid = document.getElementById("productGrid");
  const checkoutList = document.getElementById("checkoutList");
  const checkoutTotal = document.getElementById("checkoutTotal");
  const checkoutBtn = document.querySelector(".btn.checkout");

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
      alert("Out of stock!");
      return;
    }

    if (cart[id]) {
      if (cart[id].qty < product.stock_Quantity) {
        cart[id].qty++;
      } else {
        alert("No more stock available for " + product.product_Name);
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

  function renderCheckout() {
    checkoutList.innerHTML = "";
    let total = 0;

    Object.values(cart).forEach(item => {
      const subtotal = item.qty * item.price;
      total += subtotal;

      const li = document.createElement("li");
      li.textContent = `${item.name} x${item.qty} — ₱${subtotal.toLocaleString()}`;
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

      await fetch("http://localhost:5055/api/POS/Transaction", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(tx)
      });

      console.log("Checkout and transaction saved.");
    } catch (err) {
      console.error("Error during checkout:", err);
      alert("Checkout failed. Please try again.");
    }
  }

  function printReceipt(tx) {
    let receiptWindow = window.open("", "Receipt", "width=400,height=600");
    receiptWindow.document.write(`
      <h2>CRKD POS Receipt</h2>
      <p>Reference ID: ${tx.referenceId}</p>
      <p>Date: ${new Date(tx.date_time).toLocaleString()}</p>
      <hr>
      <ul>
        ${tx.items.map(item =>
          `<li>${item.name} x${item.quantity} — ₱${(item.quantity * item.price).toLocaleString()}</li>`
        ).join("")}
      </ul>
      <hr>
      <p>Total: ₱${tx.total_amount.toLocaleString()}</p>
      ${tx.payment_method === "Cash" ? `
        <p>Amount Received: ₱${tx.amount_received.toLocaleString()}</p>
        <p>Change: ₱${tx.change_given.toLocaleString()}</p>
      ` : `
        <p>Payment Method: GCash</p>
      `}
      <p>Status: ${tx.status}</p>
      <p>Thank you for your purchase!</p>
    `);
    receiptWindow.document.close();
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
      alert("Cart is empty.");
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
      alert("Insufficient amount received.");
      return;
    }

    const change = received - total;
    const tx = buildTransaction("Cash", received, change);

    await processCheckout(tx);
    hideModal("cashModal");
    askReceipt(tx);
  });

  document.getElementById("confirmGCashBtn").addEventListener("click", async () => {
    const refId = document.getElementById("gcashRefId").value.trim();

    if (!refId) {
      alert("Please enter a reference ID.");
      return;
    }

    const tx = buildTransaction("GCash", null, null, refId);

    await processCheckout(tx);
    hideModal("gcashModal");
    askReceipt(tx);
  });

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