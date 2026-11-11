namespace Crooked.Models
{
    public sealed class ForecastDailyProduct
    {
        public int Id { get; init; }
        public string ProductName { get; init; } = "";
        public string Size { get; init; } = "";
        public string Color { get; init; } = "";
        public int LowStockThreshold { get; init; }
        public string SalesVelocityRating { get; init; } = "slow";
        public int StockQuantity { get; init; }
        public StoredForecast? Forecast { get; init; }
        public int[] DailySales { get; } = new int[7];
    }

    // A row of product_forecasts.
    public sealed record StoredForecast(
        string Method,
        string Reason,
        decimal PredictedUnits,
        decimal PredictedLow,
        decimal PredictedHigh,
        int HorizonDays,
        string Trend,
        int HistoryDays,
        decimal? ModelError,
        decimal? AverageError,
        int BacktestWeeks,
        DateTime UpdatedAt);
}
