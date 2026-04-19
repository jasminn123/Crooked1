function showSection(sectionId, element) {
    document.querySelectorAll('.content-section').forEach(sec => {
        sec.style.display = 'none';
    });

    const activeSection = document.getElementById(sectionId);
    if (activeSection) {
        activeSection.style.display = 'flex';
    }

    const title = element.innerText;
    document.getElementById("welcomeHeader").innerText = title.charAt(0) + title.slice(1).toLowerCase();

    document.querySelectorAll('.nav-item').forEach(nav => nav.classList.remove('nav-active'));
    element.classList.add('nav-active');
}

const apiBase = window.location.origin.startsWith('http') ? window.location.origin : 'http://127.0.0.1:5055';

document.addEventListener("DOMContentLoaded", () => {
    const role = localStorage.getItem("userRole");
    const name = localStorage.getItem("userName");

    const profileNameElement = document.querySelector('.Owner') || document.querySelector('.jasmin') || document.querySelector('.Matthew');
    if (profileNameElement && name) {
        profileNameElement.innerText = name;
    }

    if (role && role.toLowerCase() !== "owner") {
        console.log("Restricting access for:", name);

        document.querySelectorAll('.nav-item').forEach(item => {
            const text = item.innerText.toUpperCase();
            
            if (text.includes("STAFF MANAGEMENT") || text.includes("INVENTORY")) {
                item.style.setProperty('display', 'none', 'important');
            }
        });
    }

    fetchStaff();
    fetchLogs();
    fetchProducts();
});

async function fetchStaff() {
    try {
        const response = await fetch(`${apiBase}/api/Auth/get-staff`);
        const staff = await response.json();
        let activeHtml = ""; 
        let archiveHtml = "";

        staff.forEach(s => {
            if (s.isActive) {
                activeHtml += `<tr>
                    <td>${s.fullName}</td>
                    <td>${s.username}</td>
                    <td><button onclick="toggleArchive(${s.id}, true)" class="btn-archive">Archive</button></td>
                </tr>`;
            } else {
                archiveHtml += `<tr>
                    <td>${s.fullName}</td>
                    <td>${s.username}</td>
                    <td><button onclick="toggleArchive(${s.id}, false)" class="btn-unarchive">Unarchive</button></td>
                </tr>`;
            }
        });
        document.getElementById('staffTableBody').innerHTML = activeHtml;
        document.getElementById('archiveTableBody').innerHTML = archiveHtml;
    } catch (err) { 
        console.error("Staff Fetch Error:", err); 
    }
}

async function addStaff() {
    const fullName = document.getElementById('staffName').value;
    const username = document.getElementById('staffUser').value;
    const password = document.getElementById('staffPass').value;

    if (!fullName || !username || !password) {
        alert("Please fill in all fields.");
        return;
    }

    try {
        const body = JSON.stringify({ 
            FullName: fullName,  
            Username: username,  
            Password: password   
        });
        console.log('Register staff request', apiBase, body);

        const response = await fetch(`${apiBase}/api/Auth/register-staff`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body
        });

        if (response.ok) {
            alert("Staff account created successfully!");
            document.getElementById('staffName').value = "";
            document.getElementById('staffUser').value = "";
            document.getElementById('staffPass').value = "";
            fetchStaff(); 
        } else {
            const responseText = await response.text();
            console.error('Register error', response.status, responseText);
            alert(`Staff creation failed: ${response.status} ${responseText}`);
        }
    } catch (err) {
        console.error("Connection Error:", err);
        alert("API connection failed. Make sure the app is running at http://127.0.0.1:5055.");
    }
}


async function toggleArchive(id, shouldArchive) {
    if (!confirm(`Are you sure you want to ${shouldArchive ? 'Archive' : 'Unarchive'} this staff?`)) return;
    
    try {
        const response = await fetch(`${apiBase}/api/Auth/toggle-archive/${id}?archive=${shouldArchive}`, { 
            method: 'POST' 
        });
        
        if (response.ok) { 
            console.log("Success! Refreshing lists...");
            fetchStaff(); 
            if (typeof fetchLogs === "function") fetchLogs(); 
            if (typeof loadStats === "function") loadStats();
        } else {
            alert("Failed to update status.");
        }
    } catch (err) { 
        alert("Error connecting to server."); 
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

async function fetchProducts() {
    try {
        const response = await fetch(`${apiBase}/api/Products/get-products`);
        const products = await response.json();
        
        const productGrid = document.getElementById('productGrid'); 
        if (!productGrid) return;

        let html = "";

        products.forEach(p => {
            const pName = p.Name || p.name || "Unknown Product";
            const pPrice = p.Price || p.price || 0;
            const pImage = p.ImagePath || p.imagePath || 'https://via.placeholder.com/150';

            html += `
                <div class="product-card" style="border: 1px solid #333; padding: 15px; margin: 10px; border-radius: 8px; text-align: center;">
                    <img src="${pImage}" alt="${pName}" style="width: 100%; height: 150px; object-fit: cover; border-radius: 5px;">
                    <h3 style="color: white; margin-top: 10px;">${pName}</h3>
                    <p style="color: #4caf50;">₱${pPrice}</p>
                </div>`;
        });

        productGrid.innerHTML = html || "<p style='color: white;'>No products found.</p>";

    } catch (err) {
        console.error("Error loading products:", err);
    }
}

function showSection(sectionId, element) {
    document.querySelectorAll('.content-section').forEach(sec => {
        sec.style.display = 'none';
    });

    const activeSection = document.getElementById(sectionId);
    if (activeSection) {
        activeSection.style.display = 'flex';
        
        if (sectionId === 'view-products') {
            console.log("Fetching products..."); 
            fetchProducts();
        }
    }
    
    const title = element.innerText;
    document.getElementById("welcomeHeader").innerText = title;
}

async function loadCategory(categoryName) {
    try {
        const response = await fetch(`${apiBase}/api/Products/get-by-category/${categoryName}`);
        const products = await response.json();
        
        const productGrid = document.getElementById('productGrid');
        let html = "";

        products.forEach(p => {
            html += `
                <div class="product-card">
                    <img src="${p.ImagePath || '../assets/placeholder.png'}" alt="${p.Name}">
                    <h3>${p.Name}</h3>
                    <p>₱${p.Price}</p>
                </div>`;
        });

        productGrid.innerHTML = html || "<p>No products found in this category.</p>";
    } catch (err) {
        console.error("Fetch error:", err);
    }
}

function logout() {
    localStorage.clear();
    window.location.href = "index.html";
}