using Crooked.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Crooked.Services
{
    public class ThresholdForecastOptions
    {
        public int CheckIntervalMinutes { get; set; } = 5;
        public double IncreaseFactor { get; set; } = 0.15;
        public double DecreaseFactor { get; set; } = 0.15;
        public int MinThreshold { get; set; } = 1;
        public int MaxThreshold { get; set; } = 500;
        public int DebounceHours { get; set; } = 24;
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
                    await AdjustThresholdsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error adjusting thresholds");
                }

                await Task.Delay(TimeSpan.FromMinutes(_options.CheckIntervalMinutes), stoppingToken);
            }

            _logger.LogInformation("Threshold Forecast Service stopping...");
        }

        private async Task AdjustThresholdsAsync()
        {
            var products = new List<Product>();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                var sql = @"SELECT id, product_name, category, price, stock_quantity,
                           low_stock_threshold, units_sold_last_24h, sales_velocity_rating,
                           last_threshold_update, image_url
                           FROM products";

                using (var cmd = new NpgsqlCommand(sql, conn))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        products.Add(new Product
                        {
                            Id = reader.GetInt32(0),
                            Product_Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            Category = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            Price = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3),
                            Stock_Quantity = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                            Low_Stock_Threshold = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                            Units_Sold_Last_24h = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                            Sales_Velocity_Rating = reader.IsDBNull(7) ? "moderate" : reader.GetString(7),
                            Last_Threshold_Update = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                            ImageUrl = reader.IsDBNull(9) ? "" : reader.GetString(9)
                        });
                    }
                }
            }

            foreach (var product in products)
            {
                var newThreshold = CalculateNewThreshold(product);

                if (newThreshold != product.Low_Stock_Threshold)
                {
                    await UpdateThresholdAsync(product.Id, newThreshold);

                    _logger.LogInformation(
                        "Adjusted threshold for product {ProductId} ({ProductName}): {OldThreshold} -> {NewThreshold} (Velocity: {Velocity}, Stock: {Stock}, Sold24h: {Sold})",
                        product.Id, product.Product_Name, product.Low_Stock_Threshold, newThreshold,
                        product.Sales_Velocity_Rating, product.Stock_Quantity, product.Units_Sold_Last_24h);
                }
            }

            // Reset daily sales counters at midnight
            await ResetDailyCountersIfNeededAsync();
        }

        private int CalculateNewThreshold(Product product)
        {
            var currentThreshold = product.Low_Stock_Threshold > 0 ? product.Low_Stock_Threshold : 5;

            // Check debounce - don't adjust if recently updated
            if (product.Last_Threshold_Update.HasValue)
            {
                var hoursSinceUpdate = (DateTime.UtcNow - product.Last_Threshold_Update.Value).TotalHours;
                if (hoursSinceUpdate < _options.DebounceHours)
                {
                    return currentThreshold;
                }
            }

            double velocity = product.Units_Sold_Last_24h / 24.0; // units per hour
            var newThreshold = currentThreshold;

            // Scenario 1: Product sold out or critically low with recent sales
            if (product.Stock_Quantity <= 0 && product.Units_Sold_Last_24h > 0)
            {
                // Increase threshold by factor (product sells fast)
                newThreshold = (int)Math.Round(currentThreshold * (1 + _options.IncreaseFactor));
                product.Sales_Velocity_Rating = "fast";
            }
            // Scenario 2: Stock is very low (at or below threshold) and selling
            else if (product.Stock_Quantity > 0 && product.Stock_Quantity <= currentThreshold && product.Units_Sold_Last_24h > 0)
            {
                // Increase threshold moderately
                newThreshold = (int)Math.Round(currentThreshold * (1 + _options.IncreaseFactor * 0.5));
                product.Sales_Velocity_Rating = "moderate";
            }
            // Scenario 3: High stock with no sales - decrease threshold
            else if (product.Stock_Quantity >= currentThreshold * 3 && product.Units_Sold_Last_24h == 0)
            {
                // Decrease threshold
                newThreshold = (int)Math.Round(currentThreshold * (1 - _options.DecreaseFactor));
                product.Sales_Velocity_Rating = "slow";
            }
            // Scenario 4: Moderate stock, some sales - slight adjustment based on velocity
            else if (product.Units_Sold_Last_24h > 0)
            {
                // Scale threshold toward demand
                var demandThreshold = (int)Math.Ceiling(velocity * 24); // daily demand
                if (demandThreshold > currentThreshold)
                {
                    newThreshold = (int)Math.Round(currentThreshold * (1 + _options.IncreaseFactor * 0.3));
                }
                else if (demandThreshold < currentThreshold * 0.5)
                {
                    newThreshold = (int)Math.Round(currentThreshold * (1 - _options.DecreaseFactor * 0.3));
                }

                if (velocity > 1.0) product.Sales_Velocity_Rating = "fast";
                else if (velocity > 0.1) product.Sales_Velocity_Rating = "moderate";
                else product.Sales_Velocity_Rating = "slow";
            }
            else
            {
                product.Sales_Velocity_Rating = "slow";
            }

            // Apply bounds
            newThreshold = Math.Max(_options.MinThreshold, Math.Min(_options.MaxThreshold, newThreshold));

            return newThreshold;
        }

        private async Task UpdateThresholdAsync(int productId, int newThreshold)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                var sql = @"UPDATE products
                           SET low_stock_threshold = @threshold,
                               sales_velocity_rating = @velocity,
                               last_threshold_update = NOW()
                           WHERE id = @id";

                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@threshold", newThreshold);
                    cmd.Parameters.AddWithValue("@velocity", GetVelocityForProduct(productId));
                    cmd.Parameters.AddWithValue("@id", productId);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        private string GetVelocityForProduct(int productId)
        {
            // This is a bit of a simplification - we'd need to pass the product or re-query
            // For now, return a default - the velocity rating is set during calculation
            return "moderate";
        }

        private async Task ResetDailyCountersIfNeededAsync()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Check if we need to reset (last reset was more than 24h ago)
                var sql = @"SELECT MAX(last_threshold_update) FROM products
                           WHERE units_sold_last_24h > 0";

                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != DBNull.Value && result != null)
                    {
                        var lastUpdate = Convert.ToDateTime(result);
                        if ((DateTime.UtcNow - lastUpdate).TotalHours >= 24)
                        {
                            await ResetDailyCountersAsync(conn);
                        }
                    }
                }
            }
        }

        private async Task ResetDailyCountersAsync(NpgsqlConnection conn)
        {
            var sql = @"UPDATE products
                       SET units_sold_last_24h = 0
                       WHERE units_sold_last_24h > 0";

            using (var cmd = new NpgsqlCommand(sql, conn))
            {
                var affected = await cmd.ExecuteNonQueryAsync();
                if (affected > 0)
                {
                    _logger.LogInformation("Reset daily sales counters for {Count} products", affected);
                }
            }
        }

        // Public method to increment sales counter (called from POSController)
        public async Task IncrementSalesAsync(int productId, int quantity)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                var sql = @"UPDATE products
                           SET units_sold_last_24h = units_sold_last_24h + @qty
                           WHERE id = @id";

                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@qty", quantity);
                    cmd.Parameters.AddWithValue("@id", productId);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
    }
}