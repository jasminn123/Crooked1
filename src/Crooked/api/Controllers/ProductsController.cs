using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Crooked.Models;

namespace Crooked.Controllers;

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
                        product_name    = reader["product_name"].ToString(),
                        category        = reader["category"].ToString(),
                        price           = Convert.ToDecimal(reader["price"]),
                        stock_quantity  = Convert.ToInt32(reader["stock_quantity"]),
                        imageUrl        = reader["image_url"].ToString()
                    });
                }
            }
        }
        return Ok(products);
    }

[HttpGet("get-products")]
public IActionResult GetProducts()
{
    var products = new List<object>();
    using (var connection = new MySqlConnection(_connectionString))
    {
        connection.Open();
        string sql = "SELECT id, product_name, category, price, stock_quantity, size, color, image_url FROM products";

        using (var cmd = new MySqlCommand(sql, connection))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                products.Add(new {
                    id             = Convert.ToInt32(reader["id"]),
                    product_name   = reader["product_name"].ToString(),
                    category       = reader["category"].ToString(),
                    price          = Convert.ToDecimal(reader["price"]),
                    stock_quantity = Convert.ToInt32(reader["stock_quantity"]),
                    size           = reader["size"].ToString(),
                    color          = reader["color"].ToString(),
                    image_url      = reader["image_url"].ToString()
                });
            }
        }
    }
    return Ok(products);
}

    [HttpPost("add-product")]
    public async Task<IActionResult> AddProduct([FromForm] ProductUploadDTO dto)
    {
        if (dto == null) return BadRequest("Data is empty");

        try
        {
            string imageUrl = "/images/default-product.png";
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

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                string sql = @"INSERT INTO Products 
                    (product_name, category, price, stock_quantity, size, color, image_url) 
                    VALUES (@name, @category, @price, @stock, @size, @color, @image)";

                using (var cmd = new MySqlCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("@name",     dto.ProductName);
                    cmd.Parameters.AddWithValue("@category", dto.Category);
                    cmd.Parameters.AddWithValue("@price",    dto.Price);
                    cmd.Parameters.AddWithValue("@stock",    dto.StockQuantity);
                    cmd.Parameters.AddWithValue("@size",     dto.Size);
                    cmd.Parameters.AddWithValue("@color",    dto.Color);
                    cmd.Parameters.AddWithValue("@image",    imageUrl);

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

    [HttpGet]
    [Authorize(Roles = "owner,staff")] 
    public IActionResult GetAllProducts()
    {
                return Ok(new { message = "Here are the products for everyone to see." });
    }


    [HttpPost]
    [Authorize(Roles = "owner")] 
    public IActionResult AddProduct([FromBody] ProductDto model)
    {
        if (!ModelState.IsValid) return BadRequest();

        return Ok(new { message = "Product added successfully by the owner!" });
    }
}

public class ProductDto 
{
    public string Name { get; set; }
    public decimal Price { get; set; }
}
