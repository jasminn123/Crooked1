using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Crooked.Models;
using Crooked.Services;

namespace Crooked.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ProductsController : ControllerBase
{
    private readonly string _connectionString = DatabaseConfig.ConnectionString;

    // "CRKD Tee (M / Black)", or just the name when it has no size or color.
    private static string ProductLabel(string? name, string? size, string? color)
    {
        var variant = string.Join(" / ", new[] { size, color }.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));
        return variant.Length > 0 ? $"{name?.Trim()} ({variant})" : name?.Trim() ?? "";
    }

    private Task LogProductAsync(NpgsqlConnection connection, string action) =>
        ActivityLog.WriteAsync(connection, null, SessionClaims.FullNameOf(User), ActivityTypes.Product, action);

    // Archives or restores a product and logs it. Returns the product's label, or null when no product changed.
    private async Task<string?> SetProductActiveAsync(int id, bool active)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        string? label = null;
        await using (var cmd = new NpgsqlCommand(
            "UPDATE products SET is_active = @active WHERE id = @id AND is_active = NOT @active RETURNING product_name, size, color",
            connection))
        {
            cmd.Parameters.AddWithValue("@active", active);
            cmd.Parameters.AddWithValue("@id", id);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                label = ProductLabel(reader["product_name"] as string, reader["size"] as string, reader["color"] as string);
            }
        }

        if (label != null) await LogProductAsync(connection, $"{(active ? "Restored" : "Archived")} product: {label}");
        return label;
    }

    [HttpGet("get-inventory")]
    public IActionResult GetInventory()
    {
        var products = new List<object>();
        using (var connection = new NpgsqlConnection(_connectionString))
        {
            connection.Open();
            string sql = "SELECT id, product_name, category, size, color, price, stock_quantity, low_stock_threshold, image_url FROM products WHERE is_active = true";

            using (var cmd = new NpgsqlCommand(sql, connection))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    products.Add(new {
                        product_name = reader["product_name"].ToString(),
                        category = reader["category"].ToString(),
                        size = reader["size"] == DBNull.Value ? "" : reader["size"].ToString(),
                        color = reader["color"] == DBNull.Value ? "" : reader["color"].ToString(),
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

[Authorize(Roles = UserRoles.Owner)]
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

    [Authorize(Roles = UserRoles.Owner)]
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

    [Authorize(Roles = UserRoles.Owner)]
    [HttpPost("restore-product/{id}")]
    [LiveUpdate(LiveTopics.Products, LiveTopics.Activity)]
    public async Task<IActionResult> RestoreProduct(int id)
    {
        try
        {
            if (await SetProductActiveAsync(id, true) == null)
            {
                return NotFound(new { message = "Archived product was not found." });
            }

            return Ok(new { message = "Product restored successfully." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [Authorize(Roles = UserRoles.Owner)]
    [HttpPut("update-product/{id}")]
    [LiveUpdate(LiveTopics.Products, LiveTopics.Activity)]
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

                // The price and stock before the edit, so the activity log can say what changed.
                decimal oldPrice;
                int oldStock;
                await using (var current = new NpgsqlCommand(
                    "SELECT COALESCE(price, 0), COALESCE(stock_quantity, 0) FROM products WHERE id = @id AND is_active = true", connection))
                {
                    current.Parameters.AddWithValue("@id", id);
                    await using var reader = await current.ExecuteReaderAsync();
                    if (!await reader.ReadAsync())
                    {
                        return NotFound(new { message = "Active product was not found." });
                    }
                    oldPrice = reader.GetDecimal(0);
                    oldStock = reader.GetInt32(1);
                }

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

                var changes = new List<string>();
                if (dto.Price != oldPrice) changes.Add($"price {Money.Peso(oldPrice)} → {Money.Peso(dto.Price)}");
                if (dto.StockQuantity != oldStock) changes.Add($"stock {oldStock} → {dto.StockQuantity}");
                await LogProductAsync(connection, $"Updated product: {ProductLabel(dto.ProductName, dto.Size, dto.Color)}"
                    + (changes.Count > 0 ? $" ({string.Join(", ", changes)})" : ""));
            }

            return Ok(new { message = "Product updated successfully." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [Authorize(Roles = UserRoles.Owner)]
    [HttpPost("archive-product/{id}")]
    [LiveUpdate(LiveTopics.Products, LiveTopics.Activity)]
    public async Task<IActionResult> ArchiveProduct(int id)
    {
        try
        {
            if (await SetProductActiveAsync(id, false) == null)
            {
                return NotFound(new { message = "Product was not found or is already archived." });
            }
            return Ok(new { message = "Product archive status updated successfully!" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal error: {ex.Message}");
        }
    }

    [Authorize(Roles = UserRoles.Owner)]
    [HttpPost("add-product")]
    [LiveUpdate(LiveTopics.Products, LiveTopics.Activity)]
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

                await LogProductAsync(connection, $"Added product: {ProductLabel(dto.ProductName, dto.Size, dto.Color)}"
                    + $" ({Money.Peso(dto.Price)}, stock {dto.StockQuantity})");
            }

            return Ok(new { message = "Success" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal error: {ex.Message}");
        }
    }
}
