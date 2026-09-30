using Microsoft.AspNetCore.Mvc;
using Npgsql;
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
        using (var connection = new NpgsqlConnection(_connectionString))
        {
            connection.Open();
            string sql = "SELECT id, product_name, category, price, stock_quantity, low_stock_threshold, image_url FROM products WHERE is_active = true";

            using (var cmd = new NpgsqlCommand(sql, connection))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    products.Add(new {
                        product_name = reader["product_name"].ToString(),
                        category = reader["category"].ToString(),
                        price = Convert.ToDecimal(reader["price"]),
                        stock_quantity = Convert.ToInt32(reader["stock_quantity"]),
                        low_stock_threshold = Convert.ToInt32(reader["low_stock_threshold"]),
                        imageUrl = reader["image_url"].ToString()
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
    using (var connection = new NpgsqlConnection(_connectionString))
    {
        connection.Open();
        string sql = "SELECT id, product_name, category, price, stock_quantity, size, color, image_url FROM products WHERE is_active = true";

        using (var cmd = new NpgsqlCommand(sql, connection))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                products.Add(new {
                    id = Convert.ToInt32(reader["id"]),
                    product_name = reader["product_name"].ToString(),
                    category = reader["category"].ToString(),
                    price = Convert.ToDecimal(reader["price"]),
                    stock_quantity = Convert.ToInt32(reader["stock_quantity"]),
                    size = reader["size"].ToString(),
                    color = reader["color"].ToString(),
                    image_url = reader["image_url"].ToString()
                });
            }
        }
    }
    return Ok(products);
}

    [HttpGet("get-archived-products")]
    public IActionResult GetArchivedProducts()
    {
        var products = new List<object>();
        using (var connection = new NpgsqlConnection(_connectionString))
        {
            connection.Open();
            const string sql = @"SELECT id, product_name, category, price, stock_quantity,
                                        size, color, image_url
                                 FROM products
                                 WHERE is_active = false
                                 ORDER BY product_name";
            using (var cmd = new NpgsqlCommand(sql, connection))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    products.Add(new
                    {
                        id = Convert.ToInt32(reader["id"]),
                        product_name = reader["product_name"].ToString(),
                        category = reader["category"].ToString(),
                        price = Convert.ToDecimal(reader["price"]),
                        stock_quantity = Convert.ToInt32(reader["stock_quantity"]),
                        size = reader["size"].ToString(),
                        color = reader["color"].ToString(),
                        image_url = reader["image_url"].ToString()
                    });
                }
            }
        }

        return Ok(products);
    }

    [HttpPost("restore-product/{id}")]
    public IActionResult RestoreProduct(int id)
    {
        try
        {
            using (var connection = new NpgsqlConnection(_connectionString))
            {
                connection.Open();
                const string sql = "UPDATE products SET is_active = true WHERE id = @id AND is_active = false";
                using (var cmd = new NpgsqlCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    if (cmd.ExecuteNonQuery() == 0)
                    {
                        return NotFound(new { message = "Archived product was not found." });
                    }
                }
            }

            return Ok(new { message = "Product restored successfully." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPut("update-product/{id}")]
    public async Task<IActionResult> UpdateProduct(int id, [FromForm] ProductUploadDTO dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.ProductName))
        {
            return BadRequest(new { message = "Product name is required." });
        }

        try
        {
            using (var connection = new NpgsqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string? imageUrl = null;

                if (dto.ImageFile != null && dto.ImageFile.Length > 0)
                {
                    var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/products");
                    Directory.CreateDirectory(folder);
                    var fileName = Guid.NewGuid() + "_" + Path.GetFileName(dto.ImageFile.FileName);
                    var filePath = Path.Combine(folder, fileName);
                    await using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await dto.ImageFile.CopyToAsync(stream);
                    }

                    imageUrl = "/images/products/" + fileName;
                }

                const string sql = @"
                    UPDATE products
                    SET product_name = @name,
                        category = @category,
                        price = @price,
                        stock_quantity = @stock,
                        size = @size,
                        color = @color,
                        image_url = COALESCE(@image, image_url)
                    WHERE id = @id AND is_active = true";
                using (var cmd = new NpgsqlCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("@name", dto.ProductName.Trim());
                    cmd.Parameters.AddWithValue("@category", dto.Category);
                    cmd.Parameters.AddWithValue("@price", dto.Price);
                    cmd.Parameters.AddWithValue("@stock", dto.StockQuantity);
                    cmd.Parameters.AddWithValue("@size", dto.Size ?? string.Empty);
                    cmd.Parameters.AddWithValue("@color", dto.Color ?? string.Empty);
                    cmd.Parameters.AddWithValue("@image", (object?)imageUrl ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@id", id);
                    if (await cmd.ExecuteNonQueryAsync() == 0)
                    {
                        return NotFound(new { message = "Active product was not found." });
                    }
                }
            }

            return Ok(new { message = "Product updated successfully." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPost("archive-product/{id}")]
    public IActionResult ArchiveProduct(int id)
    {
        try
        {
            using (var connection = new NpgsqlConnection(_connectionString))
            {
                connection.Open();

                string sql = "UPDATE products SET is_active = false WHERE id = @id AND is_active = true";
                using (var cmd = new NpgsqlCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    if (cmd.ExecuteNonQuery() == 0)
                    {
                        return NotFound(new { message = "Product was not found or is already archived." });
                    }
                }
            }
            return Ok(new { message = "Product archive status updated successfully!" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal error: {ex.Message}");
        }
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

            using (var connection = new NpgsqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                string sql = @"INSERT INTO Products 
                    (product_name, category, price, stock_quantity, size, color, image_url) 
                    VALUES (@name, @category, @price, @stock, @size, @color, @image)";

                using (var cmd = new NpgsqlCommand(sql, connection))
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
