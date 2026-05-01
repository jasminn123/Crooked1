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
        } else if (sectionId === 'view-pos') {
            loadPOS();
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

  // Chart
function initSalesChart() {
    const ctx = document.getElementById('salesChart').getContext('2d');
    new Chart(ctx, {
        type: 'line', 
        data: {
            labels: ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'],
            datasets: [{
                label: 'Sales (₱)',
                data: [1200, 1900, 800, 1500, 2200, 3000, 2500], 
                borderColor: '#ff0000', 
                backgroundColor: 'rgba(255, 0, 0, 0.1)',
                borderWidth: 2,
                tension: 0.4, 
                fill: true
            }]
        },
        options: {
            responsive: true,
            plugins: {
                legend: {
                  display: false 
                },
                tooltip: {
                enabled: true,
                backgroundColor: '#1a1a1a',
                titleColor: '#fff',
                bodyColor: '#ff0000'
                }
            },
            scales: {
             y: {
                beginAtZero: true,
                grid: { color: '#333' },
                ticks: { 
                    color: '#888',

                    callback: function(value) {
                    return '₱' + value.toLocaleString(); 
                    } 
                }
            },
            x: {
               grid: { display: false },
               ticks: { color: '#888' }
               }
            }
        }
    });
}

document.addEventListener('DOMContentLoaded', () => {
    initSalesChart();
    fetchLogs(); 
});

// POS
function loadPOS() {
    const posGrid = document.querySelector('.pos-products');
    if (!posGrid) {
        console.error("POS grid not found");
        return;
    }

    const products = [
        { name: "2PAC Tee", price: 399, img: "../assets/tshirt1.avif" },
        { name: "San Diego Tee", price: 499, img: "../assets/tshirt2.avif" }
    ];  

    posGrid.innerHTML = "";
    products.forEach(p => {
        posGrid.innerHTML += `
            <div class="pos-card">
                <img src="${p.img}" alt="${p.name}">
                <h3>${p.name}</h3>
                <p>₱${p.price.toLocaleString()}</p>
                <button onclick="addToCart('${p.name}', ${p.price})">Add to Cart</button>
            </div>
        `;
    });
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


const themeToggle = document.querySelector('#theme-checkbox');

themeToggle.addEventListener('change', () => {
    if (themeToggle.checked) {
        document.documentElement.setAttribute('data-theme', 'light');
        localStorage.setItem('theme', 'light');
    } else {
        document.documentElement.setAttribute('data-theme', 'dark');
        localStorage.setItem('theme', 'dark');
    }
});

const currentTheme = localStorage.getItem('theme');
if (currentTheme === 'light') {
    document.documentElement.setAttribute('data-theme', 'light');
    themeToggle.checked = true;
}

function updateDailyTotal() {
    const totalDisplay = document.getElementById('total-revenue');
    if (!totalDisplay) return; 

    const prices = document.querySelectorAll('.sales-row .order-price');
    let total = 0;

    prices.forEach(priceElement => {
        const value = parseFloat(priceElement.innerText.replace(/[₱,]/g, ''));
        if (!isNaN(value)) total += value;
    });

    totalDisplay.innerText = `₱${total.toLocaleString('en-PH', {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    })}`;
}


document.addEventListener('DOMContentLoaded', () => {
    updateDailyTotal();
    
    setTimeout(() => {
        if (typeof renderWeeklyChart === 'function') {
            renderWeeklyChart();
        } else {
            console.warn("Chart function 'renderWeeklyChart' not ready yet.");
        }
    }, 150);
});

function toggleProfileCard(event) {
  event.stopPropagation(); // prevents the outside click listener from closing it immediately
  const card = document.getElementById("profileCard");
  if (!card) return;
  card.style.display = (card.style.display === "block") ? "none" : "block";
}

document.addEventListener("click", function(event) {
  const card = document.getElementById("profileCard");
  const profileSection = document.querySelector(".profile-section");
  if (card && !profileSection.contains(event.target)) {
    card.style.display = "none";
  }
});
