document.addEventListener("DOMContentLoaded", () => {
  const productCards = document.querySelectorAll(".product-card");
  const checkoutList = document.getElementById("checkoutList");
  const checkoutTotal = document.getElementById("checkoutTotal");

  let cart = {}; // store items as { name: { qty, price } }

  productCards.forEach(card => {
    card.addEventListener("click", () => {
      const name = card.querySelector("h3").innerText;
      const priceText = card.querySelector("p").innerText.replace(/[₱,]/g, "");
      const price = parseFloat(priceText);

      // If product already in cart, increase quantity
      if (cart[name]) {
        cart[name].qty += 1;
      } else {
        cart[name] = { qty: 1, price };
      }

      renderCheckout();
    });
  });

  function renderCheckout() {
    checkoutList.innerHTML = "";
    let total = 0;

    for (const [name, item] of Object.entries(cart)) {
      const subtotal = item.qty * item.price;
      total += subtotal;

      const li = document.createElement("li");
      li.textContent = `${name} — ${item.qty} — ₱${subtotal.toLocaleString()}`;
      checkoutList.appendChild(li);
    }

    checkoutTotal.innerText = `₱${total.toLocaleString()}`;
  }

  // Clear checkout
  window.clearCheckout = function() {
    cart = {};
    checkoutList.innerHTML = "";
    checkoutTotal.innerText = "₱0.00";
  };
});
