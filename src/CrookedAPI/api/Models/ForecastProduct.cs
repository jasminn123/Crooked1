namespace Crooked.Models
{
    public sealed class ForecastProduct
    {
        public int Id { get; }
        public string Name { get; }
        public string Category { get; }
        public string? ImageUrl { get; }
        public int CurrentThreshold { get; }
        public string CurrentVelocity { get; }
        public bool CanUpdate { get; }

        public ForecastProduct(
            int id,
            string name,
            string category,
            string? imageUrl,
            int currentThreshold,
            string currentVelocity,
            bool canUpdate)
        {
            Id = id;
            Name = name;
            Category = category;
            ImageUrl = imageUrl;
            CurrentThreshold = currentThreshold;
            CurrentVelocity = currentVelocity;
            CanUpdate = canUpdate;
        }
    }
}
