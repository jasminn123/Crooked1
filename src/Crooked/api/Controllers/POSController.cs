using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Crooked.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;

namespace Crooked.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class POSController : ControllerBase
    {
        private readonly string _connectionString = DatabaseConfig.ConnectionString;

        [HttpGet("products")]
        public async Task<IActionResult> GetProducts()
        {
            var products = new List<Product>();

            using (var conn = new MySqlConnection(_connectionString))
            {
                string query = "SELECT id, product_name, price, stock_quantity, image_url FROM Products";
                var cmd = new MySqlCommand(query, conn);
                await conn.OpenAsync();
                var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                products.Add(new Product
                {
                Id = reader.GetInt32(0),
                Product_Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Price = reader.IsDBNull(2) ? 0 : reader.GetDecimal(2),
                Stock_Quantity = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                ImageUrl = reader.IsDBNull(4) ? "" : reader.GetString(4)
    });
}

            }
            return Ok(products);
        }

        [HttpPost("checkout")]
        public async Task<IActionResult> Checkout([FromBody] List<CartItem> cart)
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                foreach (var item in cart)
                {
                    var update = new MySqlCommand(
                        "UPDATE Products SET stock_quantity = stock_quantity - @qty WHERE id = @id",
                        conn
                    );
                    update.Parameters.AddWithValue("@qty", item.Quantity);
                    update.Parameters.AddWithValue("@id", item.ProductId);
                    await update.ExecuteNonQueryAsync();
                }
            }

            return Ok(new { success = true });
        }
    }

    public class CartItem
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }
}
