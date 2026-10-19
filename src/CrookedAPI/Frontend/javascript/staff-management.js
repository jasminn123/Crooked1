/* =========================
   STAFF
========================= */
async function fetchStaff() {
    try {
        const response = await fetch(`${apiBase}/api/Auth/get-staff`);
        if (!response.ok) return;
        const staff = await response.json();

        const staffBody   = document.getElementById('staffTableBody');
        const archiveBody = document.getElementById('archiveTableBody');
        if (!staffBody || !archiveBody) return;

        let activeHtml  = '';
        let archiveHtml = '';

        staff.forEach(s => {
            const row = `
                <tr>
                    <td>${s.fullName}</td>
                    <td>${s.username}</td>
                    <td>
                        <button type="button" onclick="${s.isActive ? `archiveStaff(${s.id})` : `unarchiveStaff(${s.id})`}"
                            class="btn btn-sm ${s.isActive ? 'btn-danger-outline' : 'btn-secondary'}">
                            ${s.isActive ? 'Archive' : 'Unarchive'}<span class="visually-hidden"> ${s.fullName}</span>
                        </button>
                    </td>
                </tr>
            `;
            s.isActive ? activeHtml += row : archiveHtml += row;
        });

        staffBody.innerHTML   = activeHtml;
        archiveBody.innerHTML = archiveHtml;
    } catch (error) {
        console.error('Staff Error:', error);
    }
}

/* =========================
   ARCHIVE STAFF
========================= */
async function archiveStaff(id) {
    const staff = document.getElementById('staffTableBody');
    if (!staff) return;

    const row = [...staff.children].find(r => r.querySelector(`button[onclick="archiveStaff(${id})"]`));
    const nameCell = row ? row.querySelector('td:first-child') : null;
    const staffName = nameCell ? nameCell.textContent.trim() : 'this staff member';

    showDialog(
        'Archive Staff Member',
        `Are you sure you want to archive "${staffName}"?`,
        async () => {
            try {
                const response = await fetch(`${apiBase}/api/Auth/toggle-archive/${id}?archive=true`, {
                    method: 'POST'
                });

                if (response.ok) {
                    showDialog('Staff Archived', `"${staffName}" has been archived.`);
                    fetchStaff();
                } else {
                    const errorData = await response.json().catch(() => ({}));
                    showDialog('Archive Failed', errorData.message || 'Failed to archive staff member.');
                }
            } catch (error) {
                console.error('Archive Staff Error:', error);
                showDialog('Connection Error', 'Cannot connect to the server.');
            }
        },
        null,
        { confirmText: 'Archive', danger: true }
    );
}

async function unarchiveStaff(id) {
    const staff = document.getElementById('archiveTableBody');
    if (!staff) return;

    const row = [...staff.children].find(r => r.querySelector(`button[onclick="unarchiveStaff(${id})"]`));
    const nameCell = row ? row.querySelector('td:first-child') : null;
    const staffName = nameCell ? nameCell.textContent.trim() : 'this staff member';

    showDialog(
        'Unarchive Staff Member',
        `Are you sure you want to unarchive "${staffName}"?`,
        async () => {
            try {
                const response = await fetch(`${apiBase}/api/Auth/toggle-archive/${id}?archive=false`, {
                    method: 'POST'
                });

                if (response.ok) {
                    showDialog('Staff Restored', `"${staffName}" has been unarchived.`);
                    fetchStaff();
                } else {
                    const errorData = await response.json().catch(() => ({}));
                    showDialog('Restore Failed', errorData.message || 'Failed to unarchive staff member.');
                }
            } catch (error) {
                console.error('Unarchive Staff Error:', error);
                showDialog('Connection Error', 'Cannot connect to the server.');
            }
        },
        null,
        { confirmText: 'Unarchive' }
    );
}

/* =========================
   ADD STAFF
========================= */
async function addStaff(event) {
    event?.preventDefault();
    const fullName = document.getElementById('staffName').value.trim();
    const username = document.getElementById('staffUser').value.trim();
    const password = document.getElementById('staffPass').value.trim();

    if (!fullName || !username || !password) {
        showToast('Please fill in all fields.', true);
        return;
    }

    showDialog(
        'Create Staff Account',
        `Create a staff account for "${fullName}" with the username "${username}"?`,
        () => submitStaffAccount(fullName, username, password),
        null,
        { confirmText: 'Create Account' }
    );
}

async function submitStaffAccount(fullName, username, password) {
    try {
        const response = await fetch(`${apiBase}/api/Auth/register-staff`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fullName, username, password })
        });

        if (response.ok) {
            showToast('Staff account created successfully.', false);
            document.getElementById('staffName').value = '';
            document.getElementById('staffUser').value = '';
            document.getElementById('staffPass').value = '';
            fetchStaff();
        } else {
            const err = await response.text();
            showToast('Error: ' + err, true);
        }
    } catch (error) {
        console.error('Create Staff Error:', error);
        showToast('Cannot connect to the server.', true);
    }
}

/* =========================
   PAGE LOAD
========================= */
document.addEventListener('DOMContentLoaded', fetchStaff);
