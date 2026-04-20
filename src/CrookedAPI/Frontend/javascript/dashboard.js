const apiBase = window.location.origin.startsWith('http') ? window.location.origin : 'http://127.0.0.1:5055';

function showSection(sectionId, element) {
    document.querySelectorAll('.content-section').forEach(sec => {
        sec.style.display = 'none';
    });

    const activeSection = document.getElementById(sectionId);
    if (activeSection) {
        activeSection.style.display = 'flex';
        
        if (sectionId === 'view-products') {
            fetchProducts();
        }
    }

    document.querySelectorAll('.nav-item').forEach(nav => {
        nav.classList.remove('nav-active');
    });
    element.classList.add('nav-active');
    
    document.getElementById("welcomeHeader").innerText = element.innerText;
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
    fetchArchivedStaff();
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

async function loadCategory(categoryName) {
    try {
        const response = await fetch(`${apiBase}/api/Products/get-by-category/${categoryName}`);
        const products = await response.json();
        renderProductGrid(products);
    } catch (err) {
        console.error("Fetch error:", err);
    }
}

function renderProductGrid(products) {
    const productGrid = document.getElementById('productGrid');
    if (!productGrid) return;

    let html = "";
    products.forEach(p => {
        const pName = p.name || p.Name || "Unknown Product";
        const pPrice = p.price || p.Price || 0;
        const pImage = p.imagePath || p.ImagePath || 'https://via.placeholder.com/150';

        html += `
            <div class="product-card">
                <img src="${pImage}" alt="${pName}">
                <h3>${pName}</h3>
                <p>₱${pPrice}</p>
            </div>`;
    });

    productGrid.innerHTML = html || "<p style='color: white; padding: 20px;'>No products found.</p>";
}

function filterCategory(category, buttonElement) {
    document.querySelectorAll('.btn-category').forEach(btn => btn.classList.remove('active'));
    buttonElement.classList.add('active');

    if (category === 'all') {
        fetchProducts();
    } else {
        loadCategory(category);
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
        document.getElementById('logTableBody').innerHTML = html;
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
        
        document.getElementById('staffTableBody').innerHTML = activeHtml;
        document.getElementById('archiveTableBody').innerHTML = archiveHtml;
    } catch (err) { console.error("Staff Fetch Error:", err); }
}

async function fetchArchivedStaff() {
    try {
        const response = await fetch(`${apiBase}/api/Auth/get-archived-staff`);
        const archivedStaff = await response.json();
        let html = "";
        archivedStaff.forEach(s => {
            html += `<tr><td>${s.fullName}</td><td>${s.username}</td><td>Archived</td></tr>`;
        });
        document.getElementById('archiveTableBody').innerHTML = html;
    } catch (err) { console.error("Archived Staff Fetch Error:", err); }
}


window.onload = function() {
    const dashboardBtn = document.querySelector('.nav-item'); 
    
    if (dashboardBtn) {
        showSection('view-dashboard', dashboardBtn);
    }
};

async function addStaff() {
    const name = document.getElementById('staffName').value;
    const username = document.getElementById('staffUser').value;
    const password = document.getElementById('staffPass').value;

    if (!name || !username || !password) {
        alert("Please fill in all fields");
        return;
    }

    try {
        const response = await fetch('http://localhost:5055/api/staff', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name, username, password })
        });

        if (response.ok) {
            alert("Staff Created Successfully!");
            document.getElementById('staffName').value = '';
            document.getElementById('staffUser').value = '';
            document.getElementById('staffPass').value = '';
            fetchStaff(); 
        }
    } catch (error) {
        console.error("Error adding staff:", error);
    }
}

async function archiveStaff(staffId) {
    try {
        const response = await fetch(`${apiBase}/api/Auth/archive-staff/${staffId}`, {
            method: 'PUT'
        });

        if (response.ok) {
            fetchStaff(); 
        }
    } catch (error) {
        console.error("Error archiving staff:", error);
    }
}

function logout() {
    localStorage.clear();
    window.location.href = "index.html";
}