using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Crooked.Models;
using Crooked.Services;
using System.Collections.Generic;
using System.Security.Claims;
using System;

namespace Crooked.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        // Tinatawag nya yung connection string mula sa DatabaseConfig.cs
        private readonly string _connectionString = DatabaseConfig.ConnectionString;

        // Same text as UI.passwordRulesText in ui.js, which the forms show before sending.
        private const string WeakPasswordMessage =
            "Password must be 8 to 64 characters, with an uppercase letter, a lowercase letter, a number and a symbol, and no spaces.";

        private const string FullNameClaim = SessionClaims.FullName;

        // An account on the Accounts page. Role is in lower case, like the session's role claim.
        private sealed record Account(string Username, string FullName, string Role);

        private static Account? FindAccount(NpgsqlConnection connection, int id)
        {
            using var command = new NpgsqlCommand(
                "SELECT username, COALESCE(full_name, username), COALESCE(lower(role), '') FROM users WHERE id = @id", connection);
            command.Parameters.AddWithValue("@id", id);
            using var reader = command.ExecuteReader();
            return reader.Read() ? new Account(reader.GetString(0), reader.GetString(1), reader.GetString(2)) : null;
        }

        private bool IsSignedInAs(Account account) => account.Username == User.Identity?.Name;

        // An Accounts-page change, logged under the signed-in owner's name.
        private void LogAccountChange(NpgsqlConnection connection, string action) =>
            ActivityLog.Write(connection, SessionClaims.FullNameOf(User), ActivityTypes.Account, action);

        [AllowAnonymous]
        [HttpPost("login")]
        [LiveUpdate(LiveTopics.Activity)]
        public async Task<IActionResult> Login([FromBody] User loginRequest)
        {
            try
            {
                using (var connection = new NpgsqlConnection(_connectionString))
                {
                    connection.Open();

                    string sql = "SELECT username, role, full_name FROM users WHERE username = @user AND password = @pass AND is_active = true";
                    using (var cmd = new NpgsqlCommand(sql, connection))
                    {
                        cmd.Parameters.AddWithValue("@user", loginRequest.Username);
                        cmd.Parameters.AddWithValue("@pass", loginRequest.Password);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                string username = reader["username"].ToString() ?? string.Empty;
                                string role = (reader["role"].ToString() ?? string.Empty).ToLowerInvariant();
                                string fullName = reader["full_name"].ToString() ?? string.Empty;
                                reader.Close();

                                ActivityLog.Write(connection, fullName, ActivityTypes.Login, "Logged into the system");

                                // The session cookie is what the API checks from now on; the role the
                                // browser keeps in localStorage only decides which buttons it shows.
                                var identity = new ClaimsIdentity(new[]
                                {
                                    new Claim(ClaimTypes.Name, username),
                                    new Claim(ClaimTypes.Role, role),
                                    new Claim(FullNameClaim, fullName)
                                }, CookieAuthenticationDefaults.AuthenticationScheme);
                                await HttpContext.SignInAsync(
                                    CookieAuthenticationDefaults.AuthenticationScheme,
                                    new ClaimsPrincipal(identity),
                                    new AuthenticationProperties { IsPersistent = false });

                                return Ok(new { role, fullName });
                            }
                        }
                    }
                }
                return Unauthorized(new { message = "Account archived or invalid credentials." });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Database Error: " + ex.Message);
                return StatusCode(500, new { message = "Database Error: " + ex.Message });
            }
        }

        // Ends the session by deleting the cookie. Anonymous so an expired session can still clear it;
        // only a signed-in user's logout is written to the activity log.
        [AllowAnonymous]
        [HttpPost("logout")]
        [LiveUpdate(LiveTopics.Activity)]
        public async Task<IActionResult> Logout()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                try
                {
                    using var connection = new NpgsqlConnection(_connectionString);
                    connection.Open();
                    ActivityLog.Write(connection, SessionClaims.FullNameOf(User), ActivityTypes.Login, "Logged out of the system");
                }
                catch (Exception ex)
                {
                    // Logging out must still work when the log can't be written.
                    Console.WriteLine($"Logout Log Error: {ex.Message}");
                }
            }

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return NoContent();
        }

        // Who is signed in. session.js calls it when the browser shows a page again from Back/Forward.
        [HttpGet("session")]
        public IActionResult CurrentSession() => Ok(new
        {
            username = User.Identity?.Name,
            role = User.FindFirstValue(ClaimTypes.Role),
            fullName = User.FindFirstValue(FullNameClaim)
        });

        [Authorize(Roles = UserRoles.Owner)]
        [HttpGet("dashboard-stats")]
        public IActionResult GetStats()
        {
            try
            {
                using (var connection = new NpgsqlConnection(_connectionString))
                {
                    connection.Open();
                    var sql = "SELECT COUNT(*) FROM users WHERE is_active = true";
                    using (var cmd = new NpgsqlCommand(sql, connection))
                    {
                        var count = Convert.ToInt32(cmd.ExecuteScalar());
                        return Ok(new { totalUsers = count });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // Archives or restores any account except the signed-in one, so there is always an active owner.
        // An archived account's open sessions end at once (AccountSessions).
        [Authorize(Roles = UserRoles.Owner)]
        [HttpPost("toggle-archive/{id}")]
        [LiveUpdate(LiveTopics.Staff, LiveTopics.Activity)]
        public IActionResult ToggleArchive(int id, [FromQuery] bool archive)
        {
            try
            {
                using (var connection = new NpgsqlConnection(_connectionString))
                {
                    connection.Open();

                    var account = FindAccount(connection, id);
                    if (account == null)
                    {
                        return NotFound(new { message = "Account not found." });
                    }
                    if (IsSignedInAs(account))
                    {
                        return BadRequest(new { message = "You can't archive the account you are signed in with." });
                    }

                    const string updateSql = "UPDATE users SET is_active = @status WHERE id = @id";
                    using (var cmd = new NpgsqlCommand(updateSql, connection))
                    {
                        cmd.Parameters.AddWithValue("@status", !archive);
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }
                    AccountSessions.Forget(account.Username);

                    LogAccountChange(connection, archive ? $"Archived: {account.FullName}" : $"Unarchived: {account.FullName}");
                }

                return Ok(new { message = archive ? "Account archived." : "Account restored." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // Every account for the Accounts page: owners first, then by name. isSelf marks the signed-in
        // account, whose role and archive buttons the page doesn't show.
        [Authorize(Roles = UserRoles.Owner)]
        [HttpGet("get-staff")]
        public IActionResult GetStaff()
        {
            var staffList = new List<object>();
            using (var connection = new NpgsqlConnection(_connectionString))
            {
                connection.Open();
                var sql = @"SELECT id, full_name, username, lower(role) AS role, is_active FROM users
                            ORDER BY lower(role) = 'owner' DESC, lower(full_name), id";
                using (var cmd = new NpgsqlCommand(sql, connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        staffList.Add(new {
                            id = reader["id"],
                            fullName = reader["full_name"],
                            username = reader["username"],
                            role = reader["role"],
                            isActive = Convert.ToBoolean(reader["is_active"]),
                            isSelf = reader["username"] as string == User.Identity?.Name
                        });
                    }
                }
            }
            return Ok(staffList);
        }

        // Makes an account an owner or staff. The signed-in owner can't change their own role, so
        // there is always at least one owner. The account's open sessions end at once (AccountSessions),
        // and it gets the new role's pages when it logs in again.
        [Authorize(Roles = UserRoles.Owner)]
        [HttpPost("role/{id}")]
        [LiveUpdate(LiveTopics.Staff, LiveTopics.Activity)]
        public IActionResult ChangeRole(int id, [FromBody] ChangeRoleRequest request)
        {
            var role = UserRoles.Normalize(request.Role);
            if (role == null)
            {
                return BadRequest(new { message = "Choose Owner or Staff." });
            }

            try
            {
                using (var connection = new NpgsqlConnection(_connectionString))
                {
                    connection.Open();

                    var account = FindAccount(connection, id);
                    if (account == null)
                    {
                        return NotFound(new { message = "Account not found." });
                    }
                    if (IsSignedInAs(account))
                    {
                        return BadRequest(new { message = "You can't change the role of the account you are signed in with." });
                    }
                    if (account.Role == role)
                    {
                        return Ok(new { message = $"\"{account.FullName}\" is already {UserRoles.Display(role)}." });
                    }

                    using (var cmd = new NpgsqlCommand("UPDATE users SET role = @role WHERE id = @id", connection))
                    {
                        cmd.Parameters.AddWithValue("@role", role);
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }
                    AccountSessions.Forget(account.Username);

                    LogAccountChange(connection, $"Changed role: {account.FullName} to {UserRoles.Display(role)}");
                    return Ok(new { message = $"\"{account.FullName}\" is now {UserRoles.Display(role)}." });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [Authorize(Roles = UserRoles.Owner)]
        [HttpPost("register-staff")]
        [LiveUpdate(LiveTopics.Staff, LiveTopics.Activity)]
        public IActionResult RegisterStaff([FromBody] User request)
        {
            if (!PasswordPolicy.IsValid(request.Password))
            {
                return BadRequest(new { message = WeakPasswordMessage });
            }

            using (var connection = new NpgsqlConnection(_connectionString))
            {
                connection.Open();
                var sql = "INSERT INTO users (username, password, role, full_name, is_active) VALUES (@user, @pass, 'staff', @name, true)";
                using (var cmd = new NpgsqlCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("@user", request.Username);
                    cmd.Parameters.AddWithValue("@pass", request.Password);
                    cmd.Parameters.AddWithValue("@name", request.FullName);
                    cmd.ExecuteNonQuery();
                }
                LogAccountChange(connection, $"Created staff account: {request.FullName} ({request.Username})");
            }
            return Ok();
        }

        // Any signed-in user (owner or staff) changing their own password; the current one must match.
        [HttpPost("change-password")]
        [LiveUpdate(LiveTopics.Activity)]
        public IActionResult ChangePassword([FromBody] ChangePasswordRequest request)
        {
            // Always the signed-in account; the username sent in the form is not trusted.
            var username = User.Identity?.Name ?? string.Empty;

            if (string.IsNullOrEmpty(request.CurrentPassword))
            {
                return BadRequest(new { message = "Enter your current password." });
            }

            if (!PasswordPolicy.IsValid(request.NewPassword))
            {
                return BadRequest(new { message = WeakPasswordMessage });
            }

            if (request.NewPassword == request.CurrentPassword)
            {
                return BadRequest(new { message = "The new password must be different from your current password." });
            }

            try
            {
                using (var connection = new NpgsqlConnection(_connectionString))
                {
                    connection.Open();
                    string? fullName;

                    const string updateSql = @"UPDATE users SET password = @newPass
                        WHERE username = @user AND password = @currentPass AND is_active = true
                        RETURNING full_name";
                    using (var cmd = new NpgsqlCommand(updateSql, connection))
                    {
                        cmd.Parameters.AddWithValue("@newPass", request.NewPassword);
                        cmd.Parameters.AddWithValue("@user", username);
                        cmd.Parameters.AddWithValue("@currentPass", request.CurrentPassword);
                        fullName = cmd.ExecuteScalar()?.ToString();
                    }

                    if (fullName == null)
                    {
                        return BadRequest(new { message = "Your current password is incorrect." });
                    }

                    ActivityLog.Write(connection, fullName, ActivityTypes.Account, "Changed their password");
                }

                return Ok(new { message = "Your password has been changed." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // The owner setting another account's password from the Accounts page, without its current
        // password. The signed-in account changes its own password with change-password instead.
        [Authorize(Roles = UserRoles.Owner)]
        [HttpPost("staff-password/{id}")]
        [LiveUpdate(LiveTopics.Activity)]
        public IActionResult SetStaffPassword(int id, [FromBody] SetPasswordRequest request)
        {
            if (!PasswordPolicy.IsValid(request.NewPassword))
            {
                return BadRequest(new { message = WeakPasswordMessage });
            }

            try
            {
                using (var connection = new NpgsqlConnection(_connectionString))
                {
                    connection.Open();

                    var account = FindAccount(connection, id);
                    if (account == null)
                    {
                        return NotFound(new { message = "Account not found." });
                    }
                    if (IsSignedInAs(account))
                    {
                        return BadRequest(new { message = "Change your own password with your current password instead." });
                    }

                    using (var cmd = new NpgsqlCommand("UPDATE users SET password = @newPass WHERE id = @id", connection))
                    {
                        cmd.Parameters.AddWithValue("@newPass", request.NewPassword);
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }

                    LogAccountChange(connection, $"Changed password: {account.FullName}");
                    return Ok(new { message = $"The password for \"{account.FullName}\" has been changed." });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // Owner recovery from the login page, so no session is needed.
        [AllowAnonymous]
        [HttpPost("reset-password")]
        [LiveUpdate(LiveTopics.Activity)]
public IActionResult ResetPassword([FromBody] ResetRequest request)
{

    string masterRecoveryKey = "CRKD_OWNER_2026"; 

    if (request.RecoveryKey != masterRecoveryKey)
    {
        return BadRequest(new { message = "Invalid Recovery Key!" });
    }

    if (!PasswordPolicy.IsValid(request.NewPassword))
    {
        return BadRequest(new { message = WeakPasswordMessage });
    }

    try
    {
        using (var connection = new NpgsqlConnection(_connectionString))
        {
            connection.Open();

            var sql = "UPDATE users SET password = @newPass WHERE username = @user AND role = 'owner' RETURNING COALESCE(full_name, username)";
            using (var cmd = new NpgsqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@newPass", request.NewPassword);
                cmd.Parameters.AddWithValue("@user", request.Username);

                if (cmd.ExecuteScalar() is not string ownerName)
                {
                    return BadRequest(new { message = "Owner username not found." });
                }

                ActivityLog.Write(connection, ownerName, ActivityTypes.Account, "Reset the owner password with the recovery key");
                return Ok(new { message = "Owner password reset successfully!" });
            }
        }
    }
                catch (Exception ex)
                {
                    return StatusCode(500, new { message = ex.Message });
            }
        }
    }
}