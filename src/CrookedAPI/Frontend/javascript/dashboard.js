const role = localStorage.getItem("userRole");
    const name = localStorage.getItem("userName");

    document.addEventListener("DOMContentLoaded", function() {
        if (!role) { window.location.href = "index.html"; return; }

        document.getElementById("welcomeHeader").innerText = `Welcome, ${name || 'User'}`;
        
        if (role.toLowerCase() === "owner") {
            document.querySelectorAll('.owner-only').forEach(el => el.style.display = 'block');
            document.querySelectorAll('.owner-only-nav').forEach(el => el.style.display = 'block');
            fetchStaff(); 
            fetchLogs(); 
            loadStats(); 
        } else {
            document.getElementById('staffDashboardMsg').style.display = 'block';
            document.querySelectorAll('.owner-only-nav').forEach(el => el.style.display = 'none');
        }
    });

    async function loadStats() {
        try {
            const response = await fetch('http://localhost:5055/api/Auth/dashboard-stats');
            const data = await response.json();
            document.getElementById('userCount').innerText = data.totalUsers || "0";
        } catch (err) { console.error("Stats Error:", err); }
    }

    async function fetchStaff() {
        try {
            const response = await fetch('http://localhost:5055/api/Auth/get-staff');
            const staff = await response.json();
            let activeHtml = ""; let archiveHtml = "";

            staff.forEach(s => {
                if (s.isActive) {
                    activeHtml += `<tr><td>${s.fullName}</td><td>${s.username}</td><td><button onclick="toggleArchive(${s.id}, true)" style="background: #ff5555; color: white; padding: 6px 12px; font-size: 11px;">Archive</button></td></tr>`;
                } else {
                    archiveHtml += `<tr><td>${s.fullName}</td><td>${s.username}</td><td><button onclick="toggleArchive(${s.id}, false)" style="background: #55ff55; color: black; padding: 6px 12px; font-size: 11px;">Unarchive</button></td></tr>`;
                }
            });
            document.getElementById('staffTableBody').innerHTML = activeHtml;
            document.getElementById('archiveTableBody').innerHTML = archiveHtml;
        } catch (err) { console.error("Staff Fetch Error:", err); }
    }

    async function fetchLogs() {
        try {
            const response = await fetch('http://localhost:5055/api/Auth/get-logs');
            const logs = await response.json();
            let html = "";
            logs.forEach(log => {
                const date = new Date(log.dateOccurred).toLocaleString();
                html += `<tr><td>${log.staffName}</td><td>${log.action}</td><td>${date}</td></tr>`;
            });
            document.getElementById('logTableBody').innerHTML = html;
        } catch (err) { console.error("Logs Fetch Error:", err); }
    }

    async function toggleArchive(id, shouldArchive) {
        if (!confirm(`Are you sure?`)) return;
        try {
            const response = await fetch(`http://localhost:5055/api/Auth/toggle-archive/${id}?archive=${shouldArchive}`, { method: 'POST' });
            if (response.ok) { 
                fetchStaff(); 
                fetchLogs(); 
                loadStats();
            }
        } catch (err) { alert("Error connecting to server."); }
    }

    async function addStaff() {
        const fullName = document.getElementById('staffName').value;
        const username = document.getElementById('staffUser').value;
        const password = document.getElementById('staffPass').value;
        if(!fullName || !username || !password) return alert("Fill in all fields");

        try {
            const response = await fetch('http://localhost:5055/api/Auth/register-staff', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ Username: username, Password: password, FullName: fullName })
            });
            if (response.ok) { 
                document.getElementById('staffName').value = "";
                document.getElementById('staffUser').value = "";
                document.getElementById('staffPass').value = "";
                fetchStaff(); fetchLogs(); loadStats();
            }
        } catch (err) { console.error("Add Staff Error:", err); }
    }

    function showSection(sectionId, element) {
        document.querySelectorAll('.content-section').forEach(sec => sec.style.display = 'none');
        document.getElementById(sectionId).style.display = 'block';
        document.querySelectorAll('.nav-item').forEach(nav => nav.classList.remove('nav-active'));
        element.classList.add('nav-active');
    }
    
    function logout() {
        localStorage.clear();
        window.location.href = "index.html";
    }