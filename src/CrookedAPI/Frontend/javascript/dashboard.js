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
    fetchStaff();
    fetchLogs();
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

function logout() {
    localStorage.clear();
    window.location.href = "index.html";
}