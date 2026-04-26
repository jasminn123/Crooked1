const apiBase = window.location.origin.startsWith('http') ? window.location.origin : 'http://127.0.0.1:5055';

function showSection(sectionId, element) {
    document.querySelectorAll('.content-section').forEach(sec => {
        sec.style.display = 'none';
    });

    const activeSection = document.getElementById(sectionId);
    if (activeSection) {
        activeSection.style.display = 'block'; 
        
        if (sectionId === 'view-products') {
            fetchProducts();
        } else if (sectionId === 'view-inventory') {
            loadInventory(); 
        }
    }

    document.querySelectorAll('.nav-item').forEach(nav => {
        nav.classList.remove('nav-active', 'active');
    });
    
    if (element) {
        element.classList.add('nav-active', 'active');
        const header = document.getElementById("welcomeHeader");
        if (header) header.innerText = element.innerText;
    }
}


async function loadInventory() {
    alert("Inventory function triggered!");
    console.log("System: Fetching inventory data..."); 
    try {
        const response = await fetch(`${apiBase}/api/products/get-inventory`);
        if (!response.ok) throw new Error("Backend response was not OK");
        
        const products = await response.json();
        console.log("System: Data received successfully", products); 
        
        const tableBody = document.getElementById('inventory-list-main');
        if (!tableBody) {
            console.error("Critical: Table body 'inventory-list-main' not found in HTML.");
            return;
        }

        tableBody.innerHTML = ''; 

        products.forEach(item => {
            const isLow = item.stock_quantity <= 5;
            const status = isLow 
                ? '<span style="color: #ff4d4d; font-weight: bold;">LOW STOCK</span>' 
                : '<span style="color: #2ecc71;">OK</span>';
            
            tableBody.innerHTML += `
                <tr style="border-bottom: 1px solid #222;">
                    <td style="padding: 12px; color: white;">${item.product_name}</td>
                    <td style="padding: 12px; color: #888;">${item.category}</td>
                    <td style="padding: 12px; color: white;">₱${item.price.toLocaleString()}</td>
                    <td style="padding: 12px; color: white;">${item.stock_quantity}</td>
                    <td style="padding: 12px;">${status}</td>
                </tr>`;
        });
    } catch (error) {
        console.error("Critical Inventory Error:", error);
    }
}


document.addEventListener("DOMContentLoaded", () => {
    const role = localStorage.getItem("userRole");
    const name = localStorage.getItem("userName");

    const profileNameElement = document.querySelector('.Owner');
    if (profileNameElement && name) {
        profileNameElement.innerText = name;
    }

    if (role && role.toLowerCase() !== "owner") {
        document.querySelectorAll('.nav-item').forEach(item => {
            const text = item.innerText.toUpperCase();
            if (text.includes("STAFF MANAGEMENT") || text.includes("INVENTORY")) {
                item.style.setProperty('display', 'none', 'important');
            }
        });
    }

    fetchLogs();
    fetchStaff();
    
    // fetchArchivedStaff(); 
});


async function fetchProducts() {
    try {
        const response = await fetch(`${apiBase}/api/Products/get-products`);
        const products = await response.json();
        renderProductGrid(products);
    } catch (err) {
        console.error("Error loading products:", err);
    }
}

async function fetchLogs() {
    try {
        const response = await fetch(`${apiBase}/api/Auth/get-logs`);
        const logs = await response.json();
        let html = "";
        logs.forEach(log => {
            const date = new Date(log.dateOccurred).toLocaleString();
            html += `<tr><td>${log.staffName}</td><td>${log.action}</td><td>${date}</td></tr>`;
        });
        const logBody = document.getElementById('logTableBody');
        if (logBody) logBody.innerHTML = html;
    } catch (err) { console.error("Logs Fetch Error:", err); }
}

async function fetchStaff() {
    try {
        const response = await fetch(`${apiBase}/api/Auth/get-staff`);
        const staff = await response.json();
        let activeHtml = ""; 
        let archiveHtml = "";

        staff.forEach(s => {
            const row = `<tr>
                <td>${s.fullName}</td>
                <td>${s.username}</td>
                <td>
                    <button onclick="archiveStaff(${s.id})" class="${s.isActive ? 'btn-archive' : 'btn-unarchive'}">
                        ${s.isActive ? 'Archive' : 'Unarchive'}
                    </button>
                </td>
            </tr>`;
            if (s.isActive) activeHtml += row; else archiveHtml += row;
        });
        
        const staffBody = document.getElementById('staffTableBody');
        const archiveBody = document.getElementById('archiveTableBody');
        if (staffBody) staffBody.innerHTML = activeHtml;
        if (archiveBody) archiveBody.innerHTML = archiveHtml;
    } catch (err) { console.error("Staff Fetch Error:", err); }
}

window.onload = function() {
    const dashboardBtn = document.querySelector('.nav-item'); 
    if (dashboardBtn) {
        showSection('view-dashboard', dashboardBtn);
    }
};

function logout() {
    localStorage.clear();
    window.location.href = "index.html";
}