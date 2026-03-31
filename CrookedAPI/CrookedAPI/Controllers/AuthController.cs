using CrookedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;

namespace CrookedAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly string _connectionString;

        public AuthController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet("test-db")]
        public IActionResult TestConnection()
        {
            try
            {
                using (var connection = new MySqlConnection(_connectionString))
                {
                    connection.Open();
                    return Ok(new { message = "Successfully connected to Crooked DB! XAMPP is working." });
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Connection failed: " + ex.Message });
            }
        }
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                var sql = "SELECT * FROM users WHERE username = @user AND password = @pass";
                using (var cmd = new MySqlCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("@user", request.Username);
                    cmd.Parameters.AddWithValue("@pass", request.Password);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return Ok(new
                            {
                                message = "Login Successful!",
                                role = reader["role"].ToString(),
                                name = reader["full_name"].ToString()
                            });
                        }
                    }
                }
            }
            return Unauthorized(new { message = "Invalid username or password" });
        }
    }
}