document.addEventListener("DOMContentLoaded", async () => {
  const grid = document.getElementById("productGrid");
  const checkoutList = document.getElementById("checkoutList");
  const checkoutTotal = document.getElementById("checkoutTotal");
  const checkoutBtn = document.querySelector(".btn.checkout");

  let cart = {};

  // Load products from API
  async function loadProducts() {
    try {
      const res = await fetch("http://localhost:5055/api/POS/products"); // ✅ match your backend port
      const products = await res.json();

      grid.innerHTML = "";
      products.forEach(p => {
        const card = document.createElement("div");
        card.className = "product-card";
        card.innerHTML = `
          <img src="${p.imageUrl}" alt="${p.product_Name}">
          <h3>${p.product_Name}</h3>
          <p>₱${p.price}</p>
          <small>Stock: ${p.stock_Quantity}</small>
        `;
        card.addEventListener("click", () => addToCart(p));
        grid.appendChild(card);
      });
    } catch (err) {
      console.error("Error loading products:", err);
    }
  }

  function addToCart(product) {
    const productId = product.id || product.product_Id; // ✅ handle both field names

    if (cart[productId]) {
      cart[productId].qty += 1;
    } else {
      cart[productId] = { name: product.product_Name, price: product.price, qty: 1 };
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
      li.textContent = `${item.name} — ${item.qty} — ₱${subtotal.toLocaleString()}`;
      checkoutList.appendChild(li);
    });

    checkoutTotal.textContent = `₱${total.toLocaleString()}`;
  }

  checkoutBtn.addEventListener("click", async () => {
    try {
      const cartArray = Object.entries(cart).map(([id, item]) => ({
        productId: parseInt(id),
        quantity: item.qty
      }));

      const res = await fetch("http://localhost:5055/api/POS/checkout", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(cartArray)
      });

      const result = await res.json();
      if (result.success) {
        alert("Checkout successful!");
        cart = {};
        renderCheckout();
        loadProducts(); // refresh stock
      } else {
        alert("Checkout failed: " + result.message);
      }
    } catch (err) {
      console.error("Error during checkout:", err);
    }
  });

  window.clearCheckout = function() {
    cart = {};
    renderCheckout();
  };

  // Initial load
  loadProducts();
});
