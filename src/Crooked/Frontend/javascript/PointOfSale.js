// PointOfSale.js
let cart = {}; // global cart so clearCheckout resets correctly

document.addEventListener("DOMContentLoaded", () => {
  const grid = document.getElementById("productGrid");
  const checkoutList = document.getElementById("checkoutList");
  const checkoutTotal = document.getElementById("checkoutTotal");
  const checkoutBtn = document.querySelector(".btn.checkout");

  const paymentModal = document.getElementById("paymentModal");
  const cashModal = document.getElementById("cashModal");
  const gcashModal = document.getElementById("gcashModal");
  const receiptModal = document.getElementById("receiptModal");

  document.addEventListener("DOMContentLoaded", () => {
  const cashBtn = document.getElementById("confirmCashBtn");
  const gcashBtn = document.getElementById("confirmGCashBtn");

  console.log("Cash button element:", cashBtn);
  console.log("GCash button element:", gcashBtn);

  if (cashBtn) {
    cashBtn.addEventListener("click", () => {
      console.log("Cash payment button clicked!");
      alert("Cash payment handler fired!");
    });
  }

  if (gcashBtn) {
    gcashBtn.addEventListener("click", () => {
      console.log("GCash payment button clicked!");
      alert("GCash payment handler fired!");
    });
  }
});



  // Load products from API
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

  
  // Add product to cart with stock validation
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

  // Render checkout list
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

  // Generate unique reference ID
  function generateReferenceId() {
    return 'REF-' + Date.now() + '-' + Math.floor(Math.random() * 1000);
  }

  // Build transaction object
  function buildTransaction(method, received = null, change = null, refId = null) {
    return {
      referenceId: refId || generateReferenceId(),
      date_time: new Date().toISOString(),
      total_amount: Object.values(cart).reduce((sum, item) => sum + item.price * item.qty, 0),
      customer_id: null,
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

  // Save transaction to backend
  function saveTransaction(tx) {
    fetch("http://localhost:5055/api/POS/Transaction", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(tx)
    })
    .then(res => res.json())
    .then(result => {
      console.log("Transaction saved:", result);
    })
    .catch(err => console.error("Error saving transaction:", err));
  }

  // Print receipt
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

  // Ask receipt modal
  function askReceipt(tx) {
    receiptModal.style.display = "block";

    document.getElementById("receiptYesBtn").onclick = () => {
      printReceipt(tx); // YES → print
      receiptModal.style.display = "none";
    };

    document.getElementById("receiptNoBtn").onclick = () => {
      alert("Transaction saved without printing."); // NO → skip printing
      receiptModal.style.display = "none";
    };
  }

  // Checkout button
  checkoutBtn.addEventListener("click", () => {
    const total = Object.values(cart).reduce((sum, item) => sum + item.price * item.qty, 0);
    document.getElementById("modalTotal").textContent = `₱${total.toLocaleString()}`;
    paymentModal.style.display = "block";
  });

  // Payment method selection
  document.getElementById("cashBtn").addEventListener("click", () => {
    paymentModal.style.display = "none";
    cashModal.style.display = "block";
    document.getElementById("cashTotal").textContent = checkoutTotal.textContent;
  });

  document.getElementById("gcashBtn").addEventListener("click", () => {
    paymentModal.style.display = "none";
    gcashModal.style.display = "block";
    document.getElementById("gcashTotal").textContent = checkoutTotal.textContent;
  });

  // Live change calculation
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

  // Cash payment
  document.getElementById("confirmCashBtn").addEventListener("click", () => {
    const received = parseFloat(document.getElementById("amountReceived").value);
    const total = parseFloat(checkoutTotal.textContent.replace(/[₱,]/g, ""));
    if (isNaN(received) || received < total) {
      alert("Insufficient amount received.");
      return;
    }
    const change = received - total;
    const tx = buildTransaction("Cash", received, change);
    saveTransaction(tx);
    askReceipt(tx); // show modal
    clearCheckout();
    cashModal.style.display = "none";
  });

  // GCash payment
  document.getElementById("confirmGCashBtn").addEventListener("click", () => {
    const refId = document.getElementById("gcashRefId").value.trim();
    if (!refId) {
      alert("Please enter a reference ID.");
      return;
    }
    const tx = buildTransaction("GCash", null, null, refId);
    saveTransaction(tx);
    askReceipt(tx); // show modal
    clearCheckout();
    gcashModal.style.display = "none";
  });

  // Profile dropdown logic
  const userIcon = document.getElementById("userIcon");
  const profileCard = document.getElementById("profileCard");
  const profileSection = document.querySelector(".profile-section");

  userIcon.addEventListener("click", (event) => {
    event.stopPropagation();
    profileCard.style.display = (profileCard.style.display === "block") ? "none" : "block";
  });

  document.addEventListener("click", (event) => {
    if (!profileSection.contains(event.target)) {
      profileCard.style.display = "none";
    }
  });

  // Initial load
  loadProducts();
});

// Global clearCheckout so HTML onclick works
function clearCheckout() {
  const checkoutList = document.getElementById("checkoutList");
  const checkoutTotal = document.getElementById("checkoutTotal");

  // Clear UI
  checkoutList.innerHTML = "";
  checkoutTotal.textContent = "₱0.00";

  // Reset cart object
  cart = {};

  // Hide any open modals
  document.getElementById("paymentModal").style.display = "none";
  document.getElementById("cashModal").style.display = "none";
  document.getElementById("gcashModal").style.display = "none";
  document.getElementById("receiptModal").style.display = "none";
}

document.getElementById("confirmCashBtn").addEventListener("click", () => {
  console.log("Cash payment button clicked!");
  // rest of logic...
});

