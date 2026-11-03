namespace Crooked.Models
{
    public sealed class ThresholdForecastOptions
    {
        public int CheckIntervalMinutes { get; set; } = 60;
        public int MinThreshold { get; set; } = 1;
        public int MaxThreshold { get; set; } = 500;
        public int DebounceHours { get; set; } = 24;
        // Low-stock threshold = this many days of predicted demand.
        public int CoverageDays { get; set; } = 2;
        // Days of daily sales the model learns from, and the days it predicts.
        public int HistoryDays { get; set; } = 90;
        public int HorizonDays { get; set; } = 7;
        // A product needs this many days since its first sale before the model is used;
        // until then its 7-day average is the forecast.
        public int MinHistoryDays { get; set; } = 28;
        // The restock suggestion covers this many days of predicted demand.
        public int RestockDays { get; set; } = 14;
        // Predicted sales this many percent above or below the last 4 weeks count as a trend.
        public int TrendChangePercent { get; set; } = 25;
    }
}
