using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Crooked.Models;
using System.Collections.Generic;
using System.Linq;
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

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                string query = "SELECT id, product_name, price, stock_quantity, image_url FROM Products WHERE is_active = true";
                var cmd = new NpgsqlCommand(query, conn);
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
            if (cart == null || cart.Count == 0 || cart.Any(item => item.Quantity <= 0))
            {
                return BadRequest(new { message = "The cart must contain items with valid quantities." });
            }

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                using (var transaction = await conn.BeginTransactionAsync())
                {
                    try
                    {
                        foreach (var item in cart)
                        {
                            await using var updateStock = new NpgsqlCommand(
                                @"UPDATE products
                                  SET stock_quantity = stock_quantity - @qty
                                  WHERE id = @id AND is_active = TRUE AND stock_quantity >= @qty",
                                conn, transaction);
                            updateStock.Parameters.AddWithValue("@qty", item.Quantity);
                            updateStock.Parameters.AddWithValue("@id", item.ProductId);
                            if (await updateStock.ExecuteNonQueryAsync() == 0)
                            {
                                await transaction.RollbackAsync();
                                return Conflict(new { message = "A product is unavailable or has insufficient stock." });
                            }

                            await using var recordSales = new NpgsqlCommand(
                                @"INSERT INTO product_daily_sales (product_id, sale_date, quantity_sold)
                                  VALUES (@id, (NOW() AT TIME ZONE 'Asia/Manila')::date, @qty)
                                  ON CONFLICT (product_id, sale_date)
                                  DO UPDATE SET quantity_sold = product_daily_sales.quantity_sold + EXCLUDED.quantity_sold",
                                conn, transaction);
                            recordSales.Parameters.AddWithValue("@id", item.ProductId);
                            recordSales.Parameters.AddWithValue("@qty", item.Quantity);
                            await recordSales.ExecuteNonQueryAsync();
                        }

                        await transaction.CommitAsync();
                    }
                    catch (Exception)
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
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