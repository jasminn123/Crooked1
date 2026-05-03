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
                    stock_quantity = Convert.ToInt32(reader["stock_quantity"])
                });
            }
        }
    }
    return Ok(products);
}
}