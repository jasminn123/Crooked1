namespace Crooked.Models
{
    public sealed class ForecastProduct
    {
        public int Id { get; }
        public string Name { get; }
        public string? ImageUrl { get; }
        public int CurrentThreshold { get; }
        public string CurrentVelocity { get; }
        public int UnitsSoldLast7Days { get; }
        public bool CanUpdate { get; }

        public ForecastProduct(
            int id,
            string name,
            string? imageUrl,
            int currentThreshold,
            string currentVelocity,
            int unitsSoldLast7Days,
            bool canUpdate)
        {
            Id = id;
            Name = name;
            ImageUrl = imageUrl;
            CurrentThreshold = currentThreshold;
            CurrentVelocity = currentVelocity;
            UnitsSoldLast7Days = unitsSoldLast7Days;
            CanUpdate = canUpdate;
        }
    }
}
