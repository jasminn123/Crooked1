using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Crooked.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        using (var conn = new MySqlConnection(_connectionString))
        {
            conn.Open();

            string sql = @"INSERT INTO Transactions 
                           (date_time, total_amount, customer_id, status) 
                           VALUES (@dateTime, @total, @customerId, @status)";

            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@dateTime", tx.Date_Time);
                cmd.Parameters.AddWithValue("@total", tx.Total_Amount);
                cmd.Parameters.AddWithValue("@customerId", tx.Customer_Id ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@status", tx.Status ?? "Completed");

                cmd.ExecuteNonQuery();
            }
        }

        return Ok(new { message = "Transaction saved successfully", tx });
    }
    catch (Exception ex)
    {
        // Return a JSON object, not raw exception text
        return StatusCode(500, new { error = ex.Message });
    }
}
}
}