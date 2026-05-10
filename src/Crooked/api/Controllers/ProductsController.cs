using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;

namespace Crooked.api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ProductsController : ControllerBase
{
    
    private readonly string _connectionString = DatabaseConfig.ConnectionString;

    [HttpGet("get-inventory")]
    public IActionResult GetInventory()
{
    var products = new List<object>();
    using (var connection = new MySqlConnection(_connectionString))
    {
        connection.Open();
        string sql = "SELECT id, product_name, category, price, stock_quantity, image_url FROM products";    
            
        using (var cmd = new MySqlCommand(sql, connection))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                products.Add(new {
                    product_name = reader["product_name"].ToString(), 
                    category = reader["category"].ToString(),
                    price = Convert.ToDecimal(reader["price"]),
                    stock_quantity = Convert.ToInt32(reader["stock_quantity"]),
                    imageUrl = reader["image_url"].ToString()
                });
            }
        }
    }
    return Ok(products);
}

public class ProductUploadDTO {
    public string ProductName { get; set; }
    public string Category { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public string Size { get; set; }
    public string Color { get; set; }
    public IFormFile ImageFile { get; set; } // This handles the actual image
}

[HttpPost("add-product")]
public async Task<IActionResult> AddProduct([FromForm] ProductUploadDTO dto) {
    if (dto == null) return BadRequest("Data is empty");

    try {
        // Handle Image Saving
        string imageUrl = "/images/default-product.png"; // Default
        if (dto.ImageFile != null) {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/products");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            string fileName = Guid.NewGuid().ToString() + "_" + dto.ImageFile.FileName;
            string filePath = Path.Combine(folder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create)) {
                await dto.ImageFile.CopyToAsync(stream);
            }
            imageUrl = "/images/products/" + fileName;
        }

        // Map DTO to your Database Model
        var product = new Product {
            product_name = dto.ProductName,
            category = dto.Category,
            price = dto.Price,
            stock_quantity = dto.StockQuantity,
            size = dto.Size,
            color = dto.Color,
            image_url = imageUrl
        };

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Success" });
    }
    catch (Exception ex) {
        return StatusCode(500, $"Internal error: {ex.Message}");
    }
}
}