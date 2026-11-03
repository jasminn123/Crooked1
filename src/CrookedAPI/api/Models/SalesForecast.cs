namespace Crooked.Models
{
    // Predicted units sold over the next HorizonDays, with a 90% range when the model made it.
    // Method is SalesForecaster.MachineLearning or SalesForecaster.Average, and Reason says why the
    // average was used. ModelError and AverageError are each method's average miss, in units per
    // week, when both were tested on the product's last BacktestWeeks weeks.
    public sealed record SalesForecast(
        string Method,
        float PredictedUnits,
        float LowUnits,
        float HighUnits,
        string Trend,
        int HistoryDays)
    {
        public string Reason { get; init; } = "";
        public float? ModelError { get; init; }
        public float? AverageError { get; init; }
        public int BacktestWeeks { get; init; }

        // A size or color's part of a product-level forecast.
        public SalesForecast Share(float share) => this with
        {
            PredictedUnits = PredictedUnits * share,
            LowUnits = LowUnits * share,
            HighUnits = HighUnits * share,
            ModelError = ModelError * share,
            AverageError = AverageError * share
        };
    }
}
