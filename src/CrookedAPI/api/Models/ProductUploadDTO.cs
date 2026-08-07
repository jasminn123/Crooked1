using Microsoft.AspNetCore.Http;

namespace Crooked.Models
{
    public class ProductUploadDTO
    {
        public string ProductName { get; set; }
        public string Category { get; set; }
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }
        public string Size { get; set; }
        public string Color { get; set; }
        public IFormFile ImageFile { get; set; }
    }
}