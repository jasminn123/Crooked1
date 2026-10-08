using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Crooked.Models;

namespace Crooked.Services
{
    public class ThresholdForecastService : BackgroundService
    {
        private readonly string _connectionString;
        private readonly ILogger<ThresholdForecastService> _logger;
        private readonly ThresholdForecastOptions _options;

        public ThresholdForecastService(
            ILogger<ThresholdForecastService> logger,
            IOptions<ThresholdForecastOptions> options)
        {
            _logger = logger;
            _options = options.Value;
            _connectionString = Crooked.DatabaseConfig.ConnectionString;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Threshold Forecast Service starting...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await AdjustThresholdsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error adjusting thresholds");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(_options.CheckIntervalMinutes), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.LogInformation("Threshold Forecast Service stopping...");
        }

        private async Task AdjustThresholdsAsync(CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string query = @"
                SELECT p.id, p.product_name, p.image_url, p.low_stock_threshold,
                       p.sales_velocity_rating,
                       COALESCE(SUM(s.quantity_sold), 0) AS units_sold_last_7_days,
                       COALESCE(p.last_threshold_update IS NULL OR
                           p.last_threshold_update <= NOW() - (@debounce_hours * INTERVAL '1 hour'), TRUE)
                           AS can_update
                FROM products p
                LEFT JOIN product_daily_sales s
                    ON s.product_id = p.id
                    AND s.sale_date >= (NOW() AT TIME ZONE 'Asia/Manila')::date - 6
                    AND s.sale_date <= (NOW() AT TIME ZONE 'Asia/Manila')::date
                WHERE p.is_active = TRUE
                GROUP BY p.id, p.product_name, p.low_stock_threshold, p.last_threshold_update";

            var products = new List<ForecastProduct>();
            await using (var command = new NpgsqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@debounce_hours", _options.DebounceHours);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    products.Add(new ForecastProduct(
                        reader.GetInt32(0),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.GetInt32(3),
                        reader.IsDBNull(4) ? "slow" : reader.GetString(4),
                        Convert.ToInt32(reader.GetInt64(5)),
                        reader.GetBoolean(6)));
                }
            }

            foreach (var product in products)
            {
                if (!product.CanUpdate)
                {
                    continue;
                }

                var averageDailySales = product.UnitsSoldLast7Days / 7.0;
                var threshold = Math.Clamp(
                    (int)Math.Ceiling(averageDailySales * _options.CoverageDays),
                    _options.MinThreshold,
                    _options.MaxThreshold);
                var velocity = averageDailySales >= 3 ? "fast" : averageDailySales >= 1 ? "moderate" : "slow";

                const string update = @"
                    UPDATE products
                    SET low_stock_threshold = @threshold,
                        sales_velocity_rating = @velocity,
                        last_threshold_update = NOW()
                    WHERE id = @id
                      AND (low_stock_threshold IS DISTINCT FROM @threshold
                           OR sales_velocity_rating IS DISTINCT FROM @velocity)";
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                await using var updateCommand = new NpgsqlCommand(update, connection, transaction);
                updateCommand.Parameters.AddWithValue("@threshold", threshold);
                updateCommand.Parameters.AddWithValue("@velocity", velocity);
                updateCommand.Parameters.AddWithValue("@id", product.Id);
                var changed = await updateCommand.ExecuteNonQueryAsync(cancellationToken);

                if (changed > 0)
                {
                    var thresholdChanged = threshold != product.CurrentThreshold;
                    var velocityChanged = velocity != product.CurrentVelocity
                        && (velocity == "fast" || velocity == "slow"
                            || product.CurrentVelocity == "fast" || product.CurrentVelocity == "slow");

                    if (thresholdChanged || velocityChanged)
                    {
                        const string notificationSql = @"
                            INSERT INTO forecast_notifications
                                (product_id, product_name, image_url, old_threshold, new_threshold, old_velocity, new_velocity)
                            VALUES
                                (@id, @name, @image, @old_threshold, @new_threshold, @old_velocity, @new_velocity)";
                        await using var notificationCommand = new NpgsqlCommand(notificationSql, connection, transaction);
                        notificationCommand.Parameters.AddWithValue("@id", product.Id);
                        notificationCommand.Parameters.AddWithValue("@name", product.Name);
                        notificationCommand.Parameters.AddWithValue("@image", (object?)product.ImageUrl ?? DBNull.Value);
                        notificationCommand.Parameters.AddWithValue("@old_threshold", product.CurrentThreshold);
                        notificationCommand.Parameters.AddWithValue("@new_threshold", threshold);
                        notificationCommand.Parameters.AddWithValue("@old_velocity", product.CurrentVelocity);
                        notificationCommand.Parameters.AddWithValue("@new_velocity", velocity);
                        await notificationCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await transaction.CommitAsync(cancellationToken);
                    _logger.LogInformation(
                        "Updated forecast for {ProductName}: threshold {OldThreshold} -> {NewThreshold}; velocity {OldVelocity} -> {NewVelocity}; sales {UnitsSold} in 7 days",
                        product.Name, product.CurrentThreshold, threshold,
                        product.CurrentVelocity, velocity, product.UnitsSoldLast7Days);
                }
            }
        }
    }
}