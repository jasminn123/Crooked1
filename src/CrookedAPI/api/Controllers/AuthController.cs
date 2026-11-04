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

        private const string FullNameClaim = "full_name";

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

                                var logSql = "INSERT INTO activity_logs (staff_name, action, date_occurred) VALUES (@name, 'Logged into the system', NOW())";
                                using (var logCmd = new NpgsqlCommand(logSql, connection))
                                {
                                    logCmd.Parameters.AddWithValue("@name", fullName);
                                    logCmd.ExecuteNonQuery();
                                }

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

        // Ends the session by deleting the cookie. Anonymous so an expired session can still clear it.
        [AllowAnonymous]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
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
                    string staffName;

                    using (var nameCmd = new NpgsqlCommand(
                        "SELECT full_name FROM users WHERE id = @id AND role = 'staff'", connection))
                    {
                        nameCmd.Parameters.AddWithValue("@id", id);
                        staffName = nameCmd.ExecuteScalar()?.ToString();
                    }

                    if (staffName == null)
                    {
                        return NotFound(new { message = "Staff account not found." });
                    }

                    const string updateSql = "UPDATE users SET is_active = @status WHERE id = @id AND role = 'staff'";
                    using (var cmd = new NpgsqlCommand(updateSql, connection))
                    {
                        cmd.Parameters.AddWithValue("@status", !archive);
                        cmd.Parameters.AddWithValue("@id", id);
                        if (cmd.ExecuteNonQuery() == 0)
                        {
                            return NotFound(new { message = "Staff account not found." });
                        }
                    }

                    var logSql = "INSERT INTO activity_logs (staff_name, action, date_occurred) VALUES ('Main Owner', @action, NOW())";
                    using (var logCmd = new NpgsqlCommand(logSql, connection))
                    {
                        logCmd.Parameters.AddWithValue("@action", archive ? $"Archived: {staffName}" : $"Unarchived: {staffName}");
                        logCmd.ExecuteNonQuery();
                    }
                }

                return Ok(new { message = archive ? "Staff account archived." : "Staff account restored." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [Authorize(Roles = UserRoles.Owner)]
        [HttpGet("get-staff")]
        public IActionResult GetStaff()
        {
            var staffList = new List<object>();
            using (var connection = new NpgsqlConnection(_connectionString))
            {
                connection.Open();
                var sql = "SELECT id, full_name, username, is_active FROM users WHERE role = 'staff'";
                using (var cmd = new NpgsqlCommand(sql, connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        staffList.Add(new { 
                            id = reader["id"], 
                            fullName = reader["full_name"], 
                            username = reader["username"],
                            isActive = Convert.ToBoolean(reader["is_active"])
                        });
                    }
                }
            }
            return Ok(staffList);
        }

        [Authorize(Roles = UserRoles.Owner)]
        [HttpGet("get-logs")]
        public IActionResult GetLogs()
        {
            var logs = new List<object>();
            using (var connection = new NpgsqlConnection(_connectionString))
            {
                connection.Open();
                var sql = "SELECT staff_name, action, date_occurred FROM activity_logs ORDER BY date_occurred DESC LIMIT 10";
                using (var cmd = new NpgsqlCommand(sql, connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        // date_occurred is stored in UTC; format it in Philippine time on the server
                        // so the browser doesn't read the bare timestamp as its own local time.
                        logs.Add(new {
                            staffName = reader["staff_name"],
                            action = reader["action"],
                            dateOccurred = ShopClock.Display(Convert.ToDateTime(reader["date_occurred"]))
                        });
                    }
                }
            }
            return Ok(logs);
        }

        [Authorize(Roles = UserRoles.Owner)]
        [HttpPost("register-staff")]
        [LiveUpdate(LiveTopics.Staff)]
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

                    var logSql = "INSERT INTO activity_logs (staff_name, action, date_occurred) VALUES (@name, 'Changed their password', NOW())";
                    using (var logCmd = new NpgsqlCommand(logSql, connection))
                    {
                        logCmd.Parameters.AddWithValue("@name", fullName);
                        logCmd.ExecuteNonQuery();
                    }
                }

                return Ok(new { message = "Your password has been changed." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // The owner setting a staff member's password from Staff Management. Only staff accounts can be changed here.
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
                    string? staffName;

                    const string updateSql = "UPDATE users SET password = @newPass WHERE id = @id AND role = 'staff' RETURNING full_name";
                    using (var cmd = new NpgsqlCommand(updateSql, connection))
                    {
                        cmd.Parameters.AddWithValue("@newPass", request.NewPassword);
                        cmd.Parameters.AddWithValue("@id", id);
                        staffName = cmd.ExecuteScalar()?.ToString();
                    }

                    if (staffName == null)
                    {
                        return NotFound(new { message = "Staff account not found." });
                    }

                    var logSql = "INSERT INTO activity_logs (staff_name, action, date_occurred) VALUES ('Main Owner', @action, NOW())";
                    using (var logCmd = new NpgsqlCommand(logSql, connection))
                    {
                        logCmd.Parameters.AddWithValue("@action", $"Changed password: {staffName}");
                        logCmd.ExecuteNonQuery();
                    }

                    return Ok(new { message = $"The password for \"{staffName}\" has been changed." });
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

            var sql = "UPDATE users SET password = @newPass WHERE username = @user AND role = 'owner'";
            using (var cmd = new NpgsqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@newPass", request.NewPassword);
                cmd.Parameters.AddWithValue("@user", request.Username);
                
                int rowsAffected = cmd.ExecuteNonQuery();
                if (rowsAffected > 0)
                {
                    return Ok(new { message = "Owner password reset successfully!" });
                }
                else
                {
                    return BadRequest(new { message = "Owner username not found." });
                }
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