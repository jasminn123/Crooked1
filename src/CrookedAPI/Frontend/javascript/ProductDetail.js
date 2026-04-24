// Get product ID from URL
const params = new URLSearchParams(window.location.search);
const productId = params.get("id");

// Product data (replace with API later)
const products = {
  "1": {
    name: "2PAC TEE",
    price: 399,
    imagePath: "../assets/tshirt1.avif",
    description: "A premium cotton tee featuring a bold 2PAC graphic print.",
    sizes: ["S", "M", "L", "XL"]
  },
  "2": {
    name: "NO DEFEAT TEE",
    price: 349,
    imagePath: "../assets/tshirt2.avif",
    description: "Minimalist design with motivational text. Lightweight and breathable fabric.",
    sizes: ["S", "M", "L", "XL"]
  },
  "3": {
    name: "KEEP THE WIND TEE",
    price: 349,
    imagePath: "../assets/tshirt3.avif",
    description: "Graphic tee with a vibrant sunflower design.",
    sizes: ["S", "M", "L", "XL"]
  }
};

// Load product
const product = products[productId];

if (product) {
  document.getElementById("product-title").textContent = product.name;
  document.getElementById("product-price").textContent = `₱${product.price}.00`;
  document.getElementById("product-image").src = product.imagePath;
  document.getElementById("product-description").textContent = product.description;

  // Render sizes
  const sizeContainer = document.getElementById("size-options");
  sizeContainer.innerHTML = "";
  product.sizes.forEach(size => {
    const btn = document.createElement("button");
    btn.className = "btn btn-outline-secondary";
    btn.textContent = size;
    sizeContainer.appendChild(btn);
  });
} else {
  console.error("Product not found");
}
