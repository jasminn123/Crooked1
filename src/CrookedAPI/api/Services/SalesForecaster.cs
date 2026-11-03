using Microsoft.ML;
using Microsoft.ML.Transforms.TimeSeries;
using Crooked.Models;

namespace Crooked.Services
{
    // Predicts how many units a product will sell over the next days from its daily sales.
    //
    // The model is ML.NET's Singular Spectrum Analysis (SSA) time-series forecaster, which learns a
    // product's sales level, trend and weekly pattern from its history. A model isn't automatically
    // better than repeating last week, especially on small daily counts, so each product is backtested
    // first: the model and the 7-day average both predict each of the product's last few weeks from
    // the days before it, and the one that missed by less makes the forecast. Products without enough
    // history use the 7-day average.
    public static class SalesForecaster
    {
        public const string MachineLearning = "ml";
        public const string Average = "average";

        // Why the 7-day average was used.
        public const string ShortHistory = "short-history";
        public const string ModelLessAccurate = "model-less-accurate";
        public const string ModelFailed = "model-failed";

        public const string Up = "up";
        public const string Down = "down";
        public const string Stable = "stable";

        // One week, so the model can pick up the shop's weekly pattern (Sundays closed).
        private const int WindowSize = 7;
        private const int MaxSeriesLength = 30;
        // SSA needs more than two windows of data to train; three keeps the fit stable.
        private const int MinTrainingDays = 3 * WindowSize;
        private const int MaxBacktestWeeks = 3;
        private const int TrendBaselineDays = 28;
        private const int MinTrendDays = 14;

        public static int MinimumHistory(ThresholdForecastOptions options) =>
            Math.Max(options.MinHistoryDays, MinTrainingDays + options.HorizonDays);

        // history: units sold per complete day, oldest first, starting at the product's first sale.
        // Throws if ML.NET fails, so the caller can fall back to ForecastAverage.
        public static SalesForecast Forecast(float[] history, ThresholdForecastOptions options)
        {
            if (history.Length < MinimumHistory(options))
            {
                return ForecastAverage(history, options) with { Reason = ShortHistory };
            }

            var (modelError, averageError, weeks) = Backtest(history, options);
            var forecast = modelError < averageError
                ? ForecastWithModel(history, options)
                : ForecastAverage(history, options) with { Reason = ModelLessAccurate };
            return forecast with { ModelError = modelError, AverageError = averageError, BacktestWeeks = weeks };
        }

        // The last 7 days' average daily sales, carried over the next HorizonDays.
        public static SalesForecast ForecastAverage(float[] history, ThresholdForecastOptions options)
        {
            var predicted = RecentDailyAverage(history) * options.HorizonDays;
            return new SalesForecast(Average, predicted, predicted, predicted, Trend(history, predicted, options),
                history.Length);
        }

        private static SalesForecast ForecastWithModel(float[] history, ThresholdForecastOptions options)
        {
            var prediction = Predict(history, options.HorizonDays);

            // Days are added up before flooring at zero: on a slow seller the daily forecasts swing
            // a little above and below zero, and flooring each day would inflate the total.
            var predicted = Math.Max(0, prediction.Forecast.Sum());
            // The model gives a 90% range for each day. Errors on different days partly cancel out,
            // so the range for the whole period is the square root of the summed squared half-widths.
            var halfWidth = MathF.Sqrt(prediction.High.Zip(prediction.Low, (high, low) => MathF.Pow((high - low) / 2, 2)).Sum());
            return new SalesForecast(MachineLearning, predicted, Math.Max(0, predicted - halfWidth), predicted + halfWidth,
                Trend(history, predicted, options), history.Length);
        }

        private static SalesPrediction Predict(float[] history, int horizon)
        {
            var mlContext = new MLContext(seed: 1);
            var data = mlContext.Data.LoadFromEnumerable(history.Select(units => new SalesDay { Units = units }));
            var pipeline = mlContext.Forecasting.ForecastBySsa(
                outputColumnName: nameof(SalesPrediction.Forecast),
                inputColumnName: nameof(SalesDay.Units),
                windowSize: WindowSize,
                seriesLength: Math.Min(history.Length, MaxSeriesLength),
                trainSize: history.Length,
                horizon: horizon,
                confidenceLevel: 0.90f,
                confidenceLowerBoundColumn: nameof(SalesPrediction.Low),
                confidenceUpperBoundColumn: nameof(SalesPrediction.High));

            return pipeline.Fit(data).CreateTimeSeriesEngine<SalesDay, SalesPrediction>(mlContext).Predict();
        }

        // Predicts each of the last few weeks from the days before it, with the model and with the
        // 7-day average, and returns each method's average miss in units per week.
        private static (float ModelError, float AverageError, int Weeks) Backtest(float[] history, ThresholdForecastOptions options)
        {
            var horizon = options.HorizonDays;
            var weeks = Math.Min(MaxBacktestWeeks, (history.Length - MinTrainingDays) / horizon);
            float modelMiss = 0, averageMiss = 0;

            for (var week = 1; week <= weeks; week++)
            {
                var start = history.Length - week * horizon;
                var known = history[..start];
                var actual = history[start..(start + horizon)].Sum();
                modelMiss += Math.Abs(Math.Max(0, Predict(known, horizon).Forecast.Sum()) - actual);
                averageMiss += Math.Abs(RecentDailyAverage(known) * horizon - actual);
            }

            return (modelMiss / weeks, averageMiss / weeks, weeks);
        }

        // Compares the predicted daily sales with the average day of the last 4 weeks. A change has
        // to be at least TrendChangePercent and more than one unit over the horizon, so a single sale
        // on a slow product doesn't count as a trend. Products with under 2 weeks of history get none.
        private static string Trend(float[] history, float predictedUnits, ThresholdForecastOptions options)
        {
            if (history.Length < MinTrendDays) return "";

            var baselineDays = Math.Min(history.Length, TrendBaselineDays);
            var recent = history.TakeLast(baselineDays).Sum() / baselineDays;
            var next = predictedUnits / options.HorizonDays;
            if (Math.Abs(next - recent) * options.HorizonDays <= 1) return Stable;
            if (recent <= 0) return Up;

            var changePercent = (next - recent) / recent * 100;
            return changePercent >= options.TrendChangePercent ? Up
                : changePercent <= -options.TrendChangePercent ? Down
                : Stable;
        }

        private static float RecentDailyAverage(float[] history) =>
            history.TakeLast(WindowSize).Sum() / WindowSize;

        public sealed class SalesDay
        {
            public float Units { get; set; }
        }

        public sealed class SalesPrediction
        {
            public float[] Forecast { get; set; } = Array.Empty<float>();
            public float[] Low { get; set; } = Array.Empty<float>();
            public float[] High { get; set; } = Array.Empty<float>();
        }
    }
}
