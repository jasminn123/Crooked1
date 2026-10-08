namespace Crooked.Models
{
    public sealed class ForecastDailyProduct
    {
        public int Id { get; init; }
        public string ProductName { get; init; } = "";
        public int LowStockThreshold { get; init; }
        public string SalesVelocityRating { get; init; } = "slow";
        public int[] DailySales { get; } = new int[7];
    }
}
