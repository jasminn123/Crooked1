using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Crooked.Models;
using System;

namespace Crooked.Controllers
{
    [Route("api/POS/[controller]")]
    [ApiController]
    public class TransactionController : ControllerBase
    {
        private readonly string _connectionString = DatabaseConfig.ConnectionString;

        [HttpGet("today-revenue")]
        public IActionResult GetTodayRevenue()
        {
            try
            {
                using (var connection = new NpgsqlConnection(_connectionString))
                {
                    connection.Open();
                    const string sql = @"
                        SELECT COALESCE(SUM(total_amount), 0)
                        FROM transactions
                        WHERE status = 'Completed'
                          AND date_time >= ((NOW() AT TIME ZONE 'Asia/Manila')::date::timestamp AT TIME ZONE 'Asia/Manila')
                          AND date_time < (((NOW() AT TIME ZONE 'Asia/Manila')::date + 1)::timestamp AT TIME ZONE 'Asia/Manila')";

                    using (var command = new NpgsqlCommand(sql, connection))
                    {
                        var revenue = Convert.ToDecimal(command.ExecuteScalar());
                        return Ok(new { revenue });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Today's Revenue Error: {ex.Message}");
                return StatusCode(500, new { error = "Unable to load today's revenue." });
            }
        }

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

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    Console.WriteLine("DB connection opened");

                    string sql = @"INSERT INTO Transactions 
                                   (reference_id, date_time, total_amount, status, assisted_by) 
                                   VALUES (@referenceId, @dateTime, @total, @status, @assistedBy)";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@referenceId", tx.ReferenceId ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@dateTime", tx.Date_Time);
                        cmd.Parameters.AddWithValue("@total", tx.Total_Amount);
                        cmd.Parameters.AddWithValue("@status", tx.Status ?? "Completed");
                        cmd.Parameters.AddWithValue("@assistedBy",
                            string.IsNullOrWhiteSpace(tx.AssistedBy) ? "Unknown" : tx.AssistedBy.Trim());

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

        [HttpGet]
        public IActionResult GetTransactions()
        {
            try
            {
                var transactions = new List<object>();

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    string sql = @"SELECT transaction_id, reference_id, date_time, total_amount, status, assisted_by 
                                   FROM Transactions 
                                   ORDER BY date_time DESC";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            transactions.Add(new
                            {
                                transaction_id = reader["transaction_id"].ToString(),
                                reference_id = reader["reference_id"].ToString(),
                                date_time = DateTime.SpecifyKind(Convert.ToDateTime(reader["date_time"]), DateTimeKind.Utc).ToLocalTime().ToString("MMM dd, yyyy · hh:mm tt"),
                                total_amount = Convert.ToDecimal(reader["total_amount"]),
                                status = reader["status"].ToString(),
                                assisted_by = reader["assisted_by"].ToString()
                            });
                        }
                    }
                }

                return Ok(transactions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}