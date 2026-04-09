using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using CrookedAPI.Models;
using System.Collections.Generic;
using System;

namespace CrookedAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {

        private readonly string _connectionString = "Server=localhost;Database=crooked1;Uid=user;password=password123";

        [HttpPost("login")]
        public IActionResult Login([FromBody] User loginRequest)
        {
            try 
            {
                using (var connection = new MySqlConnection(_connectionString))
                {
                    connection.Open();

                    string sql = "SELECT role, full_name FROM users WHERE username = @user AND password = @pass AND is_active = 1";
                    using (var cmd = new MySqlCommand(sql, connection))
                    {
                        cmd.Parameters.AddWithValue("@user", loginRequest.Username);
                        cmd.Parameters.AddWithValue("@pass", loginRequest.Password);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                string role = reader["role"].ToString();
                                string fullName = reader["full_name"].ToString();
                                reader.Close(); 

                                // LOG THE LOGIN
                                var logSql = "INSERT INTO activity_logs (staff_name, action, date_occurred) VALUES (@name, 'Logged into the system', NOW())";                            
                                using (var logCmd = new MySqlCommand(logSql, connection))
                                {
                                    logCmd.Parameters.AddWithValue("@name", fullName);
                                    logCmd.ExecuteNonQuery();
                                }

                                return Ok(new { role, fullName });
                            }
                        }
                    }
                }
                return Unauthorized(new { message = "Account archived or invalid credentials." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Database Error: " + ex.Message });
            }
        }

        [HttpGet("dashboard-stats")]
        public IActionResult GetStats()
        {
            try
            {
                using (var connection = new MySqlConnection(_connectionString))
                {
                    connection.Open();
                    var sql = "SELECT COUNT(*) FROM users WHERE is_active = 1";
                    using (var cmd = new MySqlCommand(sql, connection))
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

        [HttpPost("toggle-archive/{id}")]
        public IActionResult ToggleArchive(int id, [FromQuery] bool archive)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                var sql = "UPDATE users SET is_active = @status WHERE id = @id";
                
                string staffName = "";
                using (var nameCmd = new MySqlCommand("SELECT full_name FROM users WHERE id = @id", connection)) {
                    nameCmd.Parameters.AddWithValue("@id", id);
                    staffName = nameCmd.ExecuteScalar()?.ToString();
                }

                using (var cmd = new MySqlCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("@status", archive ? 0 : 1);
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }

                var logSql = "INSERT INTO activity_logs (staff_name, action, date_occurred) VALUES ('Main Owner', @action, NOW())";                
                using (var logCmd = new MySqlCommand(logSql, connection))
                {
                    logCmd.Parameters.AddWithValue("@action", archive ? $"Archived: {staffName}" : $"Unarchived: {staffName}");
                    logCmd.ExecuteNonQuery();
                }
            }
            return Ok();
        }

        [HttpGet("get-staff")]
        public IActionResult GetStaff()
        {
            var staffList = new List<object>();
            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                var sql = "SELECT id, full_name, username, is_active FROM users WHERE role = 'staff'";
                using (var cmd = new MySqlCommand(sql, connection))
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

        [HttpGet("get-logs")]
        public IActionResult GetLogs()
        {
            var logs = new List<object>();
            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                var sql = "SELECT staff_name, action, date_occurred FROM activity_logs ORDER BY date_occurred DESC LIMIT 10";
                using (var cmd = new MySqlCommand(sql, connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        logs.Add(new { 
                            staffName = reader["staff_name"], 
                            action = reader["action"], 
                            dateOccurred = reader["date_occurred"] 
                        });
                    }
                }
            }
            return Ok(logs);
        }

        [HttpPost("register-staff")]
        public IActionResult RegisterStaff([FromBody] User request)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                var sql = "INSERT INTO users (username, password, role, full_name, is_active) VALUES (@user, @pass, 'staff', @name, 1)";
                using (var cmd = new MySqlCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("@user", request.Username);
                    cmd.Parameters.AddWithValue("@pass", request.Password);
                    cmd.Parameters.AddWithValue("@name", request.FullName);
                    cmd.ExecuteNonQuery();
                }
            }
            return Ok();
        }

        [HttpPost("reset-password")]
public IActionResult ResetPassword([FromBody] ResetRequest request)
{

    string masterRecoveryKey = "CRKD_OWNER_2026"; 

    if (request.RecoveryKey != masterRecoveryKey)
    {
        return BadRequest(new { message = "Invalid Recovery Key!" });
    }

    try
    {
        using (var connection = new MySqlConnection(_connectionString))
        {
            connection.Open();

            var sql = "UPDATE users SET password = @newPass WHERE username = @user AND role = 'owner'";
            using (var cmd = new MySqlCommand(sql, connection))
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

public class ResetRequest {
    public string Username { get; set; }
    public string RecoveryKey { get; set; }
    public string NewPassword { get; set; }
}
    }
}