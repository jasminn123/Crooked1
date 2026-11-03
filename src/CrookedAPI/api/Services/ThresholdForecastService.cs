using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Crooked.Models;

namespace Crooked.Services
{
    // Forecasts each active product's sales (SalesForecaster) and sets its low-stock threshold and
    // Fast/Moderate/Slow rating from the predicted demand. Each product is updated at most once per
    // DebounceHours; the forecasts are stored in product_forecasts for the Forecasting page.
    public class ThresholdForecastService : BackgroundService
    {
        // Sizes and colors share a product's forecast by their part of this many days' sales.
        private const int ShareDays = 28;

        private readonly string _connectionString;
        private readonly ILogger<ThresholdForecastService> _logger;
        private readonly ThresholdForecastOptions _options;
        private readonly LiveUpdates _liveUpdates;
        private bool _modelFailureLogged;

        public ThresholdForecastService(
            ILogger<ThresholdForecastService> logger,
            IOptions<ThresholdForecastOptions> options,
            LiveUpdates liveUpdates)
        {
            _logger = logger;
            _options = options.Value;
            _liveUpdates = liveUpdates;
            _connectionString = Crooked.DatabaseConfig.ConnectionString;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Threshold Forecast Service starting...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (await AdjustThresholdsAsync(stoppingToken) > 0)
                    {
                        _liveUpdates.Publish(LiveTopics.Forecast, LiveTopics.Products, LiveTopics.Notifications);
                    }
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

        // Returns how many products were updated.
        private async Task<int> AdjustThresholdsAsync(CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var products = await LoadProductsAsync(connection, cancellationToken);
            if (!products.Any(product => product.CanUpdate))
            {
                return 0;
            }

            var sales = await LoadDailySalesAsync(connection, cancellationToken);
            var updated = 0;

            // One size or color alone sells too rarely to learn from, so a product's sizes and colors
            // are forecast together and the forecast is split by each one's part of recent sales.
            foreach (var group in products.GroupBy(product => (product.Name, product.Category)))
            {
                if (!group.Any(product => product.CanUpdate))
                {
                    continue;
                }

                var groupHistory = SinceFirstSale(SumDays(group.Select(product => SalesOf(sales, product.Id))));
                var groupForecast = Forecast(groupHistory);
                var groupRecentUnits = group.Sum(product => SalesOf(sales, product.Id).TakeLast(ShareDays).Sum());

                foreach (var product in group.Where(product => product.CanUpdate))
                {
                    var share = groupRecentUnits > 0
                        ? SalesOf(sales, product.Id).TakeLast(ShareDays).Sum() / groupRecentUnits
                        : 1f / group.Count();
                    await SaveForecastAsync(connection, product, groupForecast.Share(share), cancellationToken);
                    updated++;
                }
            }

            return updated;
        }

        private SalesForecast Forecast(float[] history)
        {
            try
            {
                return SalesForecaster.Forecast(history, _options);
            }
            catch (Exception ex)
            {
                // ML.NET needs native libraries; if they can't load, keep forecasting with the average.
                if (!_modelFailureLogged)
                {
                    _logger.LogWarning(ex, "The sales forecasting model failed; using 7-day averages instead");
                    _modelFailureLogged = true;
                }

                return SalesForecaster.ForecastAverage(history, _options) with { Reason = SalesForecaster.ModelFailed };
            }
        }

        private async Task SaveForecastAsync(
            NpgsqlConnection connection,
            ForecastProduct product,
            SalesForecast forecast,
            CancellationToken cancellationToken)
        {
            var dailyDemand = MathF.Round(forecast.PredictedUnits / _options.HorizonDays, 2);
            var threshold = Math.Clamp(
                (int)Math.Ceiling(dailyDemand * _options.CoverageDays),
                _options.MinThreshold,
                _options.MaxThreshold);
            var velocity = dailyDemand >= 3 ? "fast" : dailyDemand >= 1 ? "moderate" : "slow";

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            const string update = @"
                UPDATE products
                SET low_stock_threshold = @threshold,
                    sales_velocity_rating = @velocity,
                    last_threshold_update = NOW()
                WHERE id = @id";
            await using (var updateCommand = new NpgsqlCommand(update, connection, transaction))
            {
                updateCommand.Parameters.AddWithValue("@threshold", threshold);
                updateCommand.Parameters.AddWithValue("@velocity", velocity);
                updateCommand.Parameters.AddWithValue("@id", product.Id);
                await updateCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            const string saveForecast = @"
                INSERT INTO product_forecasts
                    (product_id, method, reason, predicted_units, predicted_low, predicted_high, horizon_days,
                     trend, history_days, model_error, average_error, backtest_weeks, updated_at)
                VALUES
                    (@id, @method, @reason, @predicted, @low, @high, @horizon,
                     @trend, @history_days, @model_error, @average_error, @backtest_weeks, NOW())
                ON CONFLICT (product_id) DO UPDATE SET
                    method = EXCLUDED.method,
                    reason = EXCLUDED.reason,
                    predicted_units = EXCLUDED.predicted_units,
                    predicted_low = EXCLUDED.predicted_low,
                    predicted_high = EXCLUDED.predicted_high,
                    horizon_days = EXCLUDED.horizon_days,
                    trend = EXCLUDED.trend,
                    history_days = EXCLUDED.history_days,
                    model_error = EXCLUDED.model_error,
                    average_error = EXCLUDED.average_error,
                    backtest_weeks = EXCLUDED.backtest_weeks,
                    updated_at = EXCLUDED.updated_at";
            await using (var forecastCommand = new NpgsqlCommand(saveForecast, connection, transaction))
            {
                forecastCommand.Parameters.AddWithValue("@id", product.Id);
                forecastCommand.Parameters.AddWithValue("@method", forecast.Method);
                forecastCommand.Parameters.AddWithValue("@reason", forecast.Reason);
                forecastCommand.Parameters.AddWithValue("@predicted", Units(forecast.PredictedUnits));
                forecastCommand.Parameters.AddWithValue("@low", Units(forecast.LowUnits));
                forecastCommand.Parameters.AddWithValue("@high", Units(forecast.HighUnits));
                forecastCommand.Parameters.AddWithValue("@horizon", _options.HorizonDays);
                forecastCommand.Parameters.AddWithValue("@trend", forecast.Trend);
                forecastCommand.Parameters.AddWithValue("@history_days", forecast.HistoryDays);
                forecastCommand.Parameters.AddWithValue("@model_error",
                    forecast.ModelError is float modelError ? Units(modelError) : (object)DBNull.Value);
                forecastCommand.Parameters.AddWithValue("@average_error",
                    forecast.AverageError is float averageError ? Units(averageError) : (object)DBNull.Value);
                forecastCommand.Parameters.AddWithValue("@backtest_weeks", forecast.BacktestWeeks);
                await forecastCommand.ExecuteNonQueryAsync(cancellationToken);
            }

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
                "Updated forecast for {ProductName}: {Method} predicts {Predicted:0.0} units in {Horizon} days; threshold {OldThreshold} -> {NewThreshold}; velocity {OldVelocity} -> {NewVelocity}",
                product.Name, forecast.Method, forecast.PredictedUnits, _options.HorizonDays,
                product.CurrentThreshold, threshold, product.CurrentVelocity, velocity);
        }

        // Active products, and whether each is due an update. A product without a stored forecast is
        // always due, so forecasts appear as soon as this version first runs.
        private async Task<List<ForecastProduct>> LoadProductsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            const string query = @"
                SELECT p.id, p.product_name, COALESCE(p.category, ''), p.image_url, p.low_stock_threshold,
                       p.sales_velocity_rating,
                       p.last_threshold_update IS NULL
                           OR p.last_threshold_update <= NOW() - (@debounce_hours * INTERVAL '1 hour')
                           OR NOT EXISTS (SELECT 1 FROM product_forecasts f WHERE f.product_id = p.id) AS can_update
                FROM products p
                WHERE p.is_active = TRUE";

            var products = new List<ForecastProduct>();
            await using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddWithValue("@debounce_hours", _options.DebounceHours);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                products.Add(new ForecastProduct(
                    reader.GetInt32(0),
                    reader.IsDBNull(1) ? "" : reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetInt32(4),
                    reader.IsDBNull(5) ? "slow" : reader.GetString(5),
                    reader.GetBoolean(6)));
            }

            return products;
        }

        // Units sold per product for the last HistoryDays complete days (Philippine dates), oldest
        // first. Today is left out because it isn't over yet.
        private async Task<Dictionary<int, float[]>> LoadDailySalesAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            const string query = @"
                SELECT product_id,
                       (NOW() AT TIME ZONE 'Asia/Manila')::date - sale_date AS days_ago,
                       quantity_sold
                FROM product_daily_sales
                WHERE sale_date >= (NOW() AT TIME ZONE 'Asia/Manila')::date - @history_days
                  AND sale_date < (NOW() AT TIME ZONE 'Asia/Manila')::date";

            var sales = new Dictionary<int, float[]>();
            await using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddWithValue("@history_days", _options.HistoryDays);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var productId = reader.GetInt32(0);
                if (!sales.TryGetValue(productId, out var days))
                {
                    days = new float[_options.HistoryDays];
                    sales.Add(productId, days);
                }

                days[_options.HistoryDays - reader.GetInt32(1)] = reader.GetInt32(2);
            }

            return sales;
        }

        private float[] SalesOf(Dictionary<int, float[]> sales, int productId) =>
            sales.TryGetValue(productId, out var days) ? days : new float[_options.HistoryDays];

        private float[] SumDays(IEnumerable<float[]> products)
        {
            var total = new float[_options.HistoryDays];
            foreach (var days in products)
            {
                for (var day = 0; day < total.Length; day++)
                {
                    total[day] += days[day];
                }
            }

            return total;
        }

        // Drops the days before the first sale, when the product may not have been on sale yet.
        private static float[] SinceFirstSale(float[] days)
        {
            var first = Array.FindIndex(days, units => units > 0);
            return first < 0 ? Array.Empty<float>() : days[first..];
        }

        private static decimal Units(float value) => Math.Round((decimal)value, 2);
    }
}
