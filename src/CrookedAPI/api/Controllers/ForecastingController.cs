using Crooked.Models;
using Crooked.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Globalization;

namespace Crooked.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Forecasting, the dashboard sales chart and forecast notifications are for the owner only.
    [Authorize(Roles = UserRoles.Owner)]
    public sealed class ForecastingController : ControllerBase
    {
        private readonly string _connectionString = DatabaseConfig.ConnectionString;
        private readonly ThresholdForecastOptions _options;

        public ForecastingController(IOptions<ThresholdForecastOptions> options)
        {
            _options = options.Value;
        }

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
                       d.sale_date, COALESCE(s.quantity_sold, 0), COALESCE(p.size, ''), COALESCE(p.color, ''),
                       COALESCE(p.stock_quantity, 0), f.method, f.reason, f.predicted_units, f.predicted_low,
                       f.predicted_high, f.horizon_days, f.trend, f.history_days, f.model_error, f.average_error,
                       f.backtest_weeks, f.updated_at
                FROM products p
                CROSS JOIN generate_series(
                    ((NOW() AT TIME ZONE 'Asia/Manila')::date - 6)::timestamp,
                    ((NOW() AT TIME ZONE 'Asia/Manila')::date)::timestamp,
                    INTERVAL '1 day'
                ) AS d(sale_date)
                LEFT JOIN product_daily_sales s
                    ON s.product_id = p.id
                    AND s.sale_date = d.sale_date::date
                LEFT JOIN product_forecasts f ON f.product_id = p.id
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
                        Size = reader.GetString(6),
                        Color = reader.GetString(7),
                        LowStockThreshold = reader.GetInt32(2),
                        SalesVelocityRating = reader.IsDBNull(3) ? "slow" : reader.GetString(3),
                        StockQuantity = reader.GetInt32(8),
                        Forecast = reader.IsDBNull(9) ? null : new StoredForecast(
                            reader.GetString(9),
                            reader.GetString(10),
                            reader.GetDecimal(11),
                            reader.GetDecimal(12),
                            reader.GetDecimal(13),
                            reader.GetInt32(14),
                            reader.GetString(15),
                            reader.GetInt32(16),
                            reader.IsDBNull(17) ? null : reader.GetDecimal(17),
                            reader.IsDBNull(18) ? null : reader.GetDecimal(18),
                            reader.GetInt32(19),
                            reader.GetDateTime(20))
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
                        size = product.Size,
                        color = product.Color,
                        lowStockThreshold = product.LowStockThreshold,
                        averageDailySales = Math.Round(product.DailySales.Average(), 2),
                        salesVelocityRating = product.SalesVelocityRating,
                        stockQuantity = product.StockQuantity,
                        forecast = ForecastView(product.Forecast, product.StockQuantity),
                        dailySales = product.DailySales
                    })
            });
        }

        // The stored forecast plus what it means for the current stock, worked out now because
        // stock changes with every sale.
        private object? ForecastView(StoredForecast? forecast, int stock)
        {
            if (forecast == null) return null;

            var dailyDemand = forecast.HorizonDays > 0 ? forecast.PredictedUnits / forecast.HorizonDays : 0;
            return new
            {
                method = forecast.Method,
                reason = forecast.Reason,
                predictedUnits = forecast.PredictedUnits,
                predictedLow = forecast.PredictedLow,
                predictedHigh = forecast.PredictedHigh,
                horizonDays = forecast.HorizonDays,
                trend = forecast.Trend,
                historyDays = forecast.HistoryDays,
                modelError = forecast.ModelError,
                averageError = forecast.AverageError,
                backtestWeeks = forecast.BacktestWeeks,
                updatedAt = forecast.UpdatedAt,
                // Null when the product isn't expected to sell, so the stock won't run out.
                daysOfStockLeft = dailyDemand >= 0.01m ? (int?)Math.Floor(Math.Max(0, stock) / dailyDemand) : null,
                suggestedRestock = Math.Max(0, (int)Math.Ceiling(dailyDemand * _options.RestockDays) - stock)
            };
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
        [LiveUpdate(LiveTopics.Notifications)]
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
