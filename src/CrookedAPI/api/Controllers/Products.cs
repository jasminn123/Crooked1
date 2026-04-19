using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;

[Route("api/[controller]")]
[ApiController]
public class Products : ControllerBase
{
    private string connectionString = "Server=localhost;Database=crooked1;Uid=root;Pwd=;";

    [HttpGet("get-by-category/{category}")]
public IActionResult GetByCategory(string category)
{
    var products = new List<object>();
    string tableName = category.ToLower() == "men" ? "menclothes" : "womenclothes";

    using (var connection = new MySqlConnection(connectionString))
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