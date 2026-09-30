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

        [HttpGet("daily-sales")]
        public async Task<IActionResult> GetDailySales()
        {
            var today = DateTime.UtcNow.AddHours(8).Date;
            var dates = Enumerable.Range(0, 7)
                .Select(offset => today.AddDays(offset - 6))
                .ToList();
            var products = new Dictionary<int, DailyProductSales>();

            const string query = @"
                SELECT p.id, p.product_name, p.low_stock_threshold, p.sales_velocity_rating,
                       d.sale_date, COALESCE(s.quantity_sold, 0)
                FROM products p
                CROSS JOIN generate_series(
                    ((NOW() AT TIME ZONE 'Asia/Manila')::date - 6)::timestamp,
                    ((NOW() AT TIME ZONE 'Asia/Manila')::date)::timestamp,
                    INTERVAL '1 day'
                ) AS d(sale_date)
                LEFT JOIN product_daily_sales s
                    ON s.product_id = p.id
                    AND s.sale_date = d.sale_date::date
                WHERE p.is_active = TRUE
                ORDER BY p.id, d.sale_date";

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var id = reader.GetInt32(0);
                if (!products.TryGetValue(id, out var product))
                {
                    product = new DailyProductSales
                    {
                        Id = id,
                        ProductName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        LowStockThreshold = reader.GetInt32(2),
                        SalesVelocityRating = reader.IsDBNull(3) ? "slow" : reader.GetString(3)
                    };
                    products.Add(id, product);
                }

                var dayIndex = (reader.GetDateTime(4).Date - dates[0]).Days;
                if (dayIndex >= 0 && dayIndex < product.DailySales.Length)
                {
                    product.DailySales[dayIndex] = reader.GetInt32(5);
                }
            }

            return Ok(new
            {
                dates = dates.Select(date => date.ToString("yyyy-MM-dd")),
                products = products.Values
                    .OrderByDescending(product => product.DailySales.Sum())
                    .Select(product => new
                    {
                        id = product.Id,
                        productName = product.ProductName,
                        lowStockThreshold = product.LowStockThreshold,
                        averageDailySales = Math.Round(product.DailySales.Average(), 2),
                        salesVelocityRating = product.SalesVelocityRating,
                        dailySales = product.DailySales
                    })
            });
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

    internal sealed class DailyProductSales
    {
        public int Id { get; set; }
        public string ProductName { get; set; } = "";
        public int LowStockThreshold { get; set; }
        public string SalesVelocityRating { get; set; } = "slow";
        public int[] DailySales { get; } = new int[7];
    }
}