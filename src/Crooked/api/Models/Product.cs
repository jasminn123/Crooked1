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
        public string? ImageUrl { get; set; }
    }
}
