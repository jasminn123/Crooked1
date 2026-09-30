using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Crooked.Services
{
    public class ThresholdForecastOptions
    {
        public int CheckIntervalMinutes { get; set; } = 60;
        public int MinThreshold { get; set; } = 1;
        public int MaxThreshold { get; set; } = 500;
        public int DebounceHours { get; set; } = 24;
        public int CoverageDays { get; set; } = 2;
    }

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
                SELECT p.id, p.product_name, p.low_stock_threshold,
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
                        reader.GetInt32(2),
                        Convert.ToInt32(reader.GetInt64(3)),
                        reader.GetBoolean(4)));
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
                    WHERE id = @id";
                await using var updateCommand = new NpgsqlCommand(update, connection);
                updateCommand.Parameters.AddWithValue("@threshold", threshold);
                updateCommand.Parameters.AddWithValue("@velocity", velocity);
                updateCommand.Parameters.AddWithValue("@id", product.Id);
                await updateCommand.ExecuteNonQueryAsync(cancellationToken);

                if (threshold != product.CurrentThreshold)
                {
                    _logger.LogInformation(
                        "Adjusted low-stock threshold for {ProductName}: {OldThreshold} -> {NewThreshold}, based on {UnitsSold} units sold in 7 days",
                        product.Name, product.CurrentThreshold, threshold, product.UnitsSoldLast7Days);
                }
            }
        }

        private sealed record ForecastProduct(
            int Id,
            string Name,
            int CurrentThreshold,
            int UnitsSoldLast7Days,
            bool CanUpdate);
    }
}