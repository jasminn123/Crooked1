using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Crooked.Models;
using System;

namespace Crooked.Controllers
{
    [Route("api/POS/[controller]")]
    [ApiController]
    public class TransactionController : ControllerBase
    {
        private readonly string _connectionString = DatabaseConfig.ConnectionString;

        [HttpPost]
        public IActionResult SaveTransaction([FromBody] Transaction tx)
        {
            try
            {
                if (tx == null)
                {
                    Console.WriteLine("ERROR: Transaction object is null");
                    return BadRequest(new { error = "Transaction is null" });
                }

                Console.WriteLine($"Received: RefId={tx.ReferenceId}, Date={tx.Date_Time}, Total={tx.Total_Amount}, Status={tx.Status}");

                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    Console.WriteLine("DB connection opened");

                    string sql = @"INSERT INTO Transactions 
                                   (reference_id, date_time, total_amount, status) 
                                   VALUES (@referenceId, @dateTime, @total, @status)";

                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@referenceId", tx.ReferenceId ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@dateTime", tx.Date_Time);
                        cmd.Parameters.AddWithValue("@total", tx.Total_Amount);
                        cmd.Parameters.AddWithValue("@status", tx.Status ?? "Completed");

                        int rows = cmd.ExecuteNonQuery();
                        Console.WriteLine($"Rows affected: {rows}");
                    }
                }

                return Ok(new { message = "Transaction saved successfully", tx });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR TYPE: {ex.GetType().Name}");
                Console.WriteLine($"ERROR MESSAGE: {ex.Message}");
                Console.WriteLine($"STACK TRACE: {ex.StackTrace}");
                return StatusCode(500, new { error = ex.Message, type = ex.GetType().Name });
            }
        }
    }
}