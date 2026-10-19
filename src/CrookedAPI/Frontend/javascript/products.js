/* =========================
   PRODUCTS
========================= */
async function fetchProducts() {
    try {
        const response = await fetch(`${apiBase}/api/Products/get-products`);

        if (!response.ok) {
            showToast(`Server error: ${response.status}`, true);
            return;
        }

        const text = await response.text();
        if (!text || text.trim().length === 0) {
            console.warn('Products API returned empty response.');
            return;
        }

        try {
            const products = JSON.parse(text);
            if (typeof renderProductGrid === 'function') {
                renderProductGrid(products);
            }
            await fetchArchivedProducts();
        } catch (parseError) {
            showToast('JSON Parse Error. Raw Response: ' + text, true);
        }

    } catch (error) {
        showToast('Network Error fetching products.', true);
    }
}

/* =========================
   ARCHIVE PRODUCT
========================= */
async function archiveProduct(id) {
    const grid = document.getElementById('productGrid');
    if (!grid) return;

    const card = [...grid.children].find(c => c.querySelector(`button[onclick="archiveProduct(${id})"]`));
    const nameEl = card ? card.querySelector('h3') : null;
    const productName = nameEl ? nameEl.textContent.trim() : 'this product';

    showDialog(
        'Archive Product',
        `Are you sure you want to archive "${productName}"? It will be hidden from inventory and the POS until restored.`,
        async () => {
            try {
                const response = await fetch(`${apiBase}/api/Products/archive-product/${id}`, {
                    method: 'POST'
                });

                if (response.ok) {
                    showDialog('Product Archived', `"${productName}" has been archived.`);
                    await fetchProducts();
                } else {
                    const errorData = await response.json().catch(() => ({}));
                    showDialog('Archive Failed', errorData.message || 'Failed to archive product.');
                }
            } catch (error) {
                console.error('Archive Product Error:', error);
                showDialog('Connection Error', 'Cannot connect to the server.');
            }
        },
        null,
        { confirmText: 'Archive', danger: true }
    );
}

async function fetchArchivedProducts() {
    const grid = document.getElementById('archivedProductGrid');
    if (!grid) return;
    if (localStorage.getItem('userRole')?.toLowerCase() !== 'owner') {
        document.getElementById('archivedProductsSection')?.remove();
        return;
    }

    try {
        const response = await fetch(`${apiBase}/api/Products/get-archived-products`);
        if (!response.ok) throw new Error(`Archived products request failed (${response.status})`);

        const products = await response.json();
        grid.replaceChildren();
        if (products.length === 0) {
            const empty = document.createElement('p');
            empty.className = 'archived-products-empty';
            empty.textContent = 'No archived products.';
            grid.appendChild(empty);
            return;
        }

        products.forEach(product => {
            const card = document.createElement('article');
            card.className = 'archived-product-card';

            const image = document.createElement('img');
            image.src = product.image_url || '';
            image.alt = product.product_name;

            const info = document.createElement('div');
            info.className = 'archived-product-info';
            const name = document.createElement('strong');
            name.textContent = product.product_name;
            const description = document.createElement('span');
            description.textContent = `${product.category} · ₱${Number(product.price).toLocaleString('en-PH')}`;
            info.append(name, description);

            const restoreButton = document.createElement('button');
            restoreButton.type = 'button';
            restoreButton.className = 'btn btn-secondary btn-sm';
            restoreButton.textContent = 'Restore';
            restoreButton.setAttribute('aria-label', `Restore ${product.product_name}`);
            restoreButton.addEventListener('click', () => restoreProduct(product.id, product.product_name));

            card.append(image, info, restoreButton);
            grid.appendChild(card);
        });
    } catch (error) {
        console.error('Archived Products Error:', error);
        const message = document.createElement('p');
        message.className = 'archived-products-empty';
        message.textContent = 'Unable to load archived products. Restart the API if you just added archive support, then refresh.';
        grid.replaceChildren(message);
    }
}

async function restoreProduct(id, productName) {
    showDialog(
        'Restore Product',
        `Restore "${productName}" to active products and POS?`,
        async () => {
            try {
                const response = await fetch(`${apiBase}/api/Products/restore-product/${id}`, { method: 'POST' });
                if (!response.ok) {
                    const error = await response.json().catch(() => ({}));
                    throw new Error(error.message || `Restore failed (${response.status})`);
                }

                await fetchProducts();
                showDialog('Product Restored', `"${productName}" is active again.`);
            } catch (error) {
                console.error('Restore Product Error:', error);
                showDialog('Restore Failed', error.message || 'Unable to restore product.');
            }
        },
        null,
        { confirmText: 'Restore' }
    );
}

/* =========================
   PRODUCT MANAGEMENT
========================= */
const productsById = new Map();
let activeCategory = 'All';

function renderProductGrid(products) {
    const grid = document.getElementById('productGrid');
    const userRole = localStorage.getItem('userRole')?.toLowerCase();

    if (!grid) return;
    grid.innerHTML = '';
    productsById.clear();

    products.forEach(p => {
        productsById.set(p.id, p);
        const card = document.createElement('div');
        card.className = 'product-card';
        card.dataset.category = p.category;

        let ownerActions = '';

        if (userRole === 'owner') {
            ownerActions = `
                <div class="product-card-actions">
                    <button type="button" class="btn btn-secondary btn-sm" onclick="openEditProductModal(${p.id})">Edit<span class="visually-hidden"> ${p.product_name}</span></button>
                    <button type="button" class="btn btn-danger-outline btn-sm" onclick="archiveProduct(${p.id})">Archive<span class="visually-hidden"> ${p.product_name}</span></button>
                </div>
            `;
        }

        // Low stock is spelled out as well as coloured, so colour is not the only cue.
        const isLowStock = p.stock_quantity <= 5;
        const stockNote = p.stock_quantity === 0 ? ' (out)' : isLowStock ? ' (low)' : '';

        card.innerHTML = `
            <div class="img-placeholder">
                ${p.image_url
                    ? `<img src="${p.image_url}" alt="${p.product_name}" onerror="this.parentElement.textContent = 'NO IMAGE AVAILABLE'">`
                    : 'NO IMAGE AVAILABLE'}
            </div>

            <div class="product-card-meta">
                <span class="product-card-category">${p.category}</span>
                <span class="product-card-price">₱${p.price.toLocaleString()}</span>
            </div>

            <h3 class="product-card-title">${p.product_name}</h3>

            <div class="product-card-details">
                <span>Size: <strong>${p.size || '-'}</strong></span>
                <span>Color: <strong>${p.color || '-'}</strong></span>
                <span>Stock: <strong class="${isLowStock ? 'is-low' : ''}">${p.stock_quantity}${stockNote}</strong></span>
            </div>

            ${ownerActions}
        `;
        grid.appendChild(card);
    });

    applyProductFilters();
}

// Closing a product form with unsaved typing asks before throwing it away.
function markFormClean(form) {
    if (form) form.dataset.dirty = '';
}

function confirmDiscardChanges(form, onDiscard) {
    if (form?.dataset.dirty !== 'true') {
        onDiscard();
        return;
    }

    showDialog(
        'Discard Changes?',
        'You have unsaved changes in this form. Close it and discard them?',
        onDiscard,
        null,
        { confirmText: 'Discard', danger: true }
    );
}

function openAddModal() {
    markFormClean(document.getElementById('productForm'));
    openModalElement(document.getElementById('addProductModal'), () => closeAddModal());
}

function closeAddModal(force = false) {
    const form = document.getElementById('productForm');
    const close = () => {
        closeModalElement(document.getElementById('addProductModal'));
        form?.reset();
        markFormClean(form);
    };

    if (force) close();
    else confirmDiscardChanges(form, close);
}

function openEditProductModal(id) {
    const product = productsById.get(id);
    if (!product) {
        showDialog('Product Unavailable', 'Refresh the product list and try again.');
        return;
    }

    document.getElementById('editProdId').value = product.id;
    document.getElementById('editProdName').value = product.product_name || '';
    const categorySelect = document.getElementById('editProdCategory');
    const category = product.category || 'T-Shirts';
    if (![...categorySelect.options].some(option => option.value === category)) {
        categorySelect.add(new Option(category, category));
    }
    categorySelect.value = category;
    document.getElementById('editProdPrice').value = product.price;
    document.getElementById('editProdStock').value = product.stock_quantity;
    document.getElementById('editProdSize').value = product.size || '';
    document.getElementById('editProdColor').value = product.color || '';

    const preview = document.getElementById('editProdImagePreview');
    preview.src = product.image_url || '';
    preview.style.display = product.image_url ? 'block' : 'none';
    document.getElementById('editProdImage').value = '';
    markFormClean(document.getElementById('editProductForm'));
    openModalElement(document.getElementById('editProductModal'), () => closeEditProductModal());
}

function closeEditProductModal(force = false) {
    const form = document.getElementById('editProductForm');
    const close = () => {
        closeModalElement(document.getElementById('editProductModal'));
        markFormClean(form);
    };

    if (force) close();
    else confirmDiscardChanges(form, close);
}

async function saveProductEdit(event) {
    event.preventDefault();
    const priceText = document.getElementById('editProdPrice').value.trim();
    if (!/^\d{1,5}(?:\.\d{0,2})?$/.test(priceText)) {
        showDialog('Invalid Price', 'Enter a price with up to 5 whole-number digits and 2 decimal places.');
        return;
    }

    const productName = document.getElementById('editProdName').value.trim();
    showDialog(
        'Save Changes',
        `Save your changes to "${productName}"?`,
        () => submitProductEdit(priceText),
        null,
        { confirmText: 'Save Changes' }
    );
}

async function submitProductEdit(priceText) {
    const formData = new FormData();
    formData.append('ProductName', document.getElementById('editProdName').value.trim());
    formData.append('Category', document.getElementById('editProdCategory').value);
    formData.append('Price', priceText);
    formData.append('StockQuantity', document.getElementById('editProdStock').value);
    formData.append('Size', document.getElementById('editProdSize').value);
    formData.append('Color', document.getElementById('editProdColor').value);
    const imageFile = document.getElementById('editProdImage').files[0];
    if (imageFile) formData.append('ImageFile', imageFile);

    const id = document.getElementById('editProdId').value;
    try {
        const response = await fetch(`${apiBase}/api/Products/update-product/${id}`, {
            method: 'PUT',
            body: formData
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            const validationMessages = error.errors
                ? Object.values(error.errors).flat().join(' ')
                : '';
            throw new Error(validationMessages || error.message || error.title || `Product update failed (${response.status})`);
        }

        closeEditProductModal(true);
        await fetchProducts();
        showDialog('Product Updated', 'Product details were saved successfully.');
    } catch (error) {
        console.error('Update Product Error:', error);
        showDialog('Update Failed', error.message || 'Unable to update product.');
    }
}

async function saveProduct(event) {
    event.preventDefault();

    const priceInput = document.getElementById('prodPrice');
    const priceText = priceInput.value.trim();
    if (!/^\d{1,5}(?:\.\d{0,2})?$/.test(priceText)) {
        showDialog('Invalid Price', 'Enter a price with up to 5 whole-number digits and 2 decimal places.');
        return;
    }

    const productName = document.getElementById('prodName').value.trim();
    showDialog(
        'Add Product',
        `Add "${productName}" to the product list? It will show up in inventory and the POS.`,
        () => submitNewProduct(priceText),
        null,
        { confirmText: 'Add Product' }
    );
}

async function submitNewProduct(priceText) {
    const formData = new FormData();
    formData.append("ProductName", document.getElementById('prodName').value);
    formData.append("Category", document.getElementById('prodCategory').value);
    formData.append("Price", priceText);
    formData.append("StockQuantity", document.getElementById('prodStock').value);
    formData.append("Size", document.getElementById('prodSize').value);
    formData.append("Color", document.getElementById('prodColor').value);

    const imageFile = document.getElementById('prodImage').files[0];
    if (imageFile) formData.append("ImageFile", imageFile);

    try {
        const response = await fetch(`${apiBase}/api/Products/add-product`, {
            method: 'POST',
            body: formData
        });

        if (response.ok) {
            closeAddModal(true);
            showToast("Product successfully added to Crooked Clothing Shop!", false);
            fetchProducts();
        } else {
            const errorText = await response.text();
            console.error("Backend Error:", errorText);
            showToast("Error adding product: " + errorText, true);
        }
    } catch (error) {
        console.error("Network Error:", error);
        showToast("Cannot connect to the server. Check if XAMPP and VS are running.", true);
    }
}

// Search and the category buttons filter together.
function applyProductFilters() {
    const query = document.getElementById('productSearch')?.value.trim().toLowerCase() || '';
    document.querySelectorAll('.product-card').forEach(card => {
        const matchesCategory = activeCategory === 'All' || card.dataset.category === activeCategory;
        const matchesSearch = card.textContent.toLowerCase().includes(query);
        card.style.display = matchesCategory && matchesSearch ? '' : 'none';
    });
}

function filterProducts() {
    applyProductFilters();
}

function filterBycategory(category, button) {
    activeCategory = category;
    document.querySelectorAll('.tab-btn').forEach(btn => {
        const isActive = btn === button;
        btn.classList.toggle('active', isActive);
        btn.setAttribute('aria-pressed', String(isActive));
    });
    applyProductFilters();
}

/* =========================
   PAGE LOAD
========================= */
document.addEventListener('DOMContentLoaded', () => {
    document.getElementById('editProdImage')?.addEventListener('change', event => {
        const input = event.currentTarget;
        const preview = document.getElementById('editProdImagePreview');
        const imageFile = input.files[0];
        if (!imageFile) return;

        preview.src = URL.createObjectURL(imageFile);
        preview.style.display = 'block';
    });

    ['productForm', 'editProductForm'].forEach(id => {
        const form = document.getElementById(id);
        ['input', 'change'].forEach(type => form?.addEventListener(type, () => {
            form.dataset.dirty = 'true';
        }));
    });

    const role = localStorage.getItem('userRole');
    const isOwner = role?.toLowerCase() === 'owner';
    const archivedProductsSection = document.getElementById('archivedProductsSection');
    if (archivedProductsSection) {
        archivedProductsSection.style.display = isOwner ? '' : 'none';
    }

    if (role && !isOwner) {
        const addProductBtn = document.getElementById('addNewProductBtn');
        if (addProductBtn) addProductBtn.style.display = 'none';
    }

    fetchProducts();
});
