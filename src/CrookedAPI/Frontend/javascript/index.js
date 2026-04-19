const API_BASE = "http://localhost:5055/api/Auth";
        async function login() {
            const userField = document.getElementById('user');
            const passField = document.getElementById('pass');
            const msg = document.getElementById('message');

            const username = userField.value.trim();
            const password = passField.value.trim();

            if (!username || !password) {
                msg.style.color = "#ff5555";
                msg.innerText = "Please enter both fields.";
                return;
            }

            msg.style.color = "#888";
            msg.innerText = "Logging in...";

            try {
                const response = await fetch(`${API_BASE}/login`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ Username: username, Password: password })
                });

                if (response.ok) {
                    const result = await response.json();
                    localStorage.setItem("userRole", result.role); 
                    localStorage.setItem("userName", result.fullName || result.username || "User");

                    msg.style.color = "#4caf50";
                    msg.innerText = "Access Granted! Opening dashboard...";
                    
                    setTimeout(() => { window.location.href = "dashboard.html"; }, 1000);
                } else {
                    const errorData = await response.json();
                    msg.style.color = "#ff5555";
                    msg.innerText = errorData.message || "Invalid username or password";
                }
            } catch (err) {
                msg.style.color = "#ff5555";
                msg.innerText = "Connection failed. Check if API is running.";
            }
        }

        document.getElementById('forgot-password-link').onclick = function(e) {
            e.preventDefault();
            document.getElementById('reset-modal').style.display = 'block';
            document.getElementById('overlay').style.display = 'block';
        };

        function closeReset() {
            document.getElementById('reset-modal').style.display = 'none';
            document.getElementById('overlay').style.display = 'none';
            document.getElementById('reset-msg').innerText = "";
        }

        async function submitReset() {
            const data = {
                username: document.getElementById('reset-username').value,
                recoveryKey: document.getElementById('recovery-key').value,
                newPassword: document.getElementById('new-password').value
            };

            try {
                const response = await fetch(`${API_BASE}/reset-password`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(data)
                });

                const result = await response.json();
                const msgField = document.getElementById('reset-msg');
                msgField.innerText = result.message;

                if (response.ok) {
                    msgField.style.color = "#4caf50";
                    alert("Password updated! Please login.");
                    closeReset();
                } else {
                    msgField.style.color = "#ff5555";
                }
            } catch (err) {
                document.getElementById('reset-msg').innerText = "Server error.";
            }
        }

        document.addEventListener('keypress', (e) => { if (e.key === 'Enter') login(); });