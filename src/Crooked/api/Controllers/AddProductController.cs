using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.IO;
using System.Threading.Tasks;
using Crooked.Models;

namespace Crooked.api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AddProductController : ControllerBase
    {
        private readonly string _connectionString = DatabaseConfig.ConnectionString;

        [HttpPost("add-product")]
        public async Task<IActionResult> AddProduct([FromForm] ProductUploadDTO dto)
        {
            if (dto == null) return BadRequest("Data is empty");

            try
            {
                // Handle Image Saving
                string imageUrl = "/images/default-product.png"; // Default
                if (dto.ImageFile != null)
                {
                    string folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/products");
                    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                    string fileName = Guid.NewGuid().ToString() + "_" + dto.ImageFile.FileName;
                    string filePath = Path.Combine(folder, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await dto.ImageFile.CopyToAsync(stream);
                    }
                    imageUrl = "/images/products/" + fileName;
                }

                // Insert directly into MySQL
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string sql = @"INSERT INTO Products 
                        (product_name, category, price, stock_quantity, size, color, image_url) 
                        VALUES (@name, @category, @price, @stock, @size, @color, @image)";

                    using (var cmd = new MySqlCommand(sql, connection))
                    {
                        cmd.Parameters.AddWithValue("@name", dto.ProductName);
                        cmd.Parameters.AddWithValue("@category", dto.Category);
                        cmd.Parameters.AddWithValue("@price", dto.Price);
                        cmd.Parameters.AddWithValue("@stock", dto.StockQuantity);
                        cmd.Parameters.AddWithValue("@size", dto.Size);
                        cmd.Parameters.AddWithValue("@color", dto.Color);
                        cmd.Parameters.AddWithValue("@image", imageUrl);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                return Ok(new { message = "Success" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal error: {ex.Message}");
            }
        }
    }
}
