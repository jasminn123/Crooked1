using Crooked.Models;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Globalization;

namespace Crooked.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class ForecastingController : ControllerBase
    {
        private readonly string _connectionString = DatabaseConfig.ConnectionString;

        [HttpGet("daily-sales")]
        [HttpGet("/api/POS/daily-sales")]
        public async Task<IActionResult> GetDailySales(CancellationToken cancellationToken)
        {
            var today = DateTime.UtcNow.AddHours(8).Date;
            var dates = Enumerable.Range(0, 7)
                .Select(offset => today.AddDays(offset - 6))
                .ToList();
            var products = new Dictionary<int, ForecastDailyProduct>();

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
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt32(0);
                if (!products.TryGetValue(id, out var product))
                {
                    product = new ForecastDailyProduct
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
                dates = dates.Select(date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
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

        [HttpGet("notifications")]
        [HttpGet("/api/POS/forecast-notifications")]
        public async Task<IActionResult> GetNotifications(CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string countSql = "SELECT COUNT(*) FROM forecast_notifications WHERE is_read = FALSE";
            await using var countCommand = new NpgsqlCommand(countSql, connection);
            var unreadCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

            const string notificationsSql = @"
                SELECT id, product_id, product_name, image_url, old_threshold, new_threshold,
                       old_velocity, new_velocity, created_at, is_read
                FROM forecast_notifications
                ORDER BY created_at DESC
                LIMIT 50";
            var notifications = new List<ForecastNotification>();
            await using (var command = new NpgsqlCommand(notificationsSql, connection))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    notifications.Add(new ForecastNotification(
                        reader.GetInt64(0),
                        reader.GetInt32(1),
                        reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.GetInt32(4),
                        reader.GetInt32(5),
                        reader.GetString(6),
                        reader.GetString(7),
                        reader.GetDateTime(8),
                        reader.GetBoolean(9)));
                }
            }

            return Ok(new ForecastNotificationsResponse(unreadCount, notifications));
        }

        [HttpPost("notifications/read")]
        [HttpPost("/api/POS/forecast-notifications/read")]
        public async Task<IActionResult> MarkNotificationsRead(CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            const string sql = "UPDATE forecast_notifications SET is_read = TRUE WHERE is_read = FALSE";
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return Ok();
        }
    }
}
