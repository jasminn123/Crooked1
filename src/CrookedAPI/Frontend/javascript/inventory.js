async function loadInventory() {
    try {
        const response = await fetch('/api/products/get-inventory');
        const products = await response.json();
        
        const tableBody = document.getElementById('inventory-list-main');
        tableBody.innerHTML = ''; 

        products.forEach(item => {
            const isLow = item.stock_quantity <= 5;
            const status = isLow 
                ? '<span style="color: #ff4d4d; font-weight: bold;">Low Stock</span>' 
                : '<span style="color: #2ecc71;">In Stock</span>';
            
            tableBody.innerHTML += `
                <tr style="border-bottom: 1px solid #222;">
                    <td style="padding: 12px;">${item.name}</td>
                    <td style="padding: 12px;">${item.category}</td>
                    <td style="padding: 12px;">₱${item.price.toLocaleString()}</td>
                    <td style="padding: 12px;">${item.stock_quantity}</td>
                    <td style="padding: 12px;">${status}</td>
                </tr>
            `;
        });
    } catch (error) {
        console.error("Failed to load inventory:", error);
    }
}

const originalShowSection = showSection;
showSection = function(sectionId, element) {
    originalShowSection(sectionId, element);
    if (sectionId === 'view-inventory') {
        loadInventory();
    }
};