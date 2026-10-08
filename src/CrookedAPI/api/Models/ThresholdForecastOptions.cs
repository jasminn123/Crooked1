namespace Crooked.Models
{
    public sealed class ThresholdForecastOptions
    {
        public int CheckIntervalMinutes { get; set; } = 60;
        public int MinThreshold { get; set; } = 1;
        public int MaxThreshold { get; set; } = 500;
        public int DebounceHours { get; set; } = 24;
        public int CoverageDays { get; set; } = 2;
    }
}
