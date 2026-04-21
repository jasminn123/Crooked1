using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
namespace CrookedAPI.api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class Products : ControllerBase
{
    // Tinatawag nya yung connection string mula sa DatabaseConfig.cs
    private readonly string _connectionString = DatabaseConfig.ConnectionString;

[HttpGet("get-products")]
public IActionResult GetAllProducts()
{
    var products = new List<object>();
    using (var connection = new MySqlConnection(_connectionString))
    {
        connection.Open();
        string sql = "SELECT id, name, price, image_path FROM products"; 
        
        using (var cmd = new MySqlCommand(sql, connection))
        {
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    products.Add(new {
                        Name = reader["name"].ToString(),
                        Price = Convert.ToDecimal(reader["price"]),
                        ImagePath = reader["image_path"].ToString()
                    });
                }
            }
        }
    }
    return Ok(products);
}

    [HttpGet("get-by-category/{category}")]
public IActionResult GetByCategory(string category)
{
    var products = new List<object>();
    string tableName = category.ToLower() == "men" ? "menclothes" : "womenclothes";

    using (var connection = new MySqlConnection(_connectionString))
    {
        connection.Open();
        string sql = $"SELECT * FROM {tableName}"; 
        using (var cmd = new MySqlCommand(sql, connection))
        {
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    products.Add(new {
                        Id = reader["id"],
                        Name = reader["name"],
                        Price = reader["price"],
                        ImagePath = reader["image_path"]
                    });
                }
            }
        }
    }
    return Ok(products);
}
}
