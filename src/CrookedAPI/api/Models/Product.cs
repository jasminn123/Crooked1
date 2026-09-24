using System;

namespace Crooked.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string Product_Name { get; set; }
        public string Category { get; set; }
        public string Size { get; set; }
        public string Color { get; set; }
        public decimal Price { get; set; }
        public int Stock_Quantity { get; set; }
        public int Low_Stock_Threshold { get; set; }

        // New properties for AI forecasting
        public int Units_Sold_Last_24h { get; set; }
        public string Sales_Velocity_Rating { get; set; } // "fast", "moderate", "slow"
        public DateTime? Last_Threshold_Update { get; set; }
        public string ImageUrl { get; set; }
    }
}