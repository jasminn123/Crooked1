using System.IO;
using Microsoft.Extensions.FileProviders;
using MySql.Data.MySqlClient;
using CrookedAPI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddCors(options => {
    options.AddPolicy("AllowAll", policy => {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

EnsureDatabaseSetup();

app.UseCors("AllowAll");
app.UseAuthorization();

var frontendProvider = new PhysicalFileProvider(
    Path.Combine(builder.Environment.ContentRootPath, "Frontend"));
var pageProvider = new PhysicalFileProvider(
    Path.Combine(builder.Environment.ContentRootPath, "Frontend", "page"));

app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = pageProvider,
    RequestPath = ""
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = pageProvider,
    RequestPath = ""
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = frontendProvider,
    RequestPath = ""
});

app.MapControllers();

app.Run();

void EnsureDatabaseSetup()
{
    var dbName = DatabaseConfig.DatabaseName;
    using var rootConnection = new MySqlConnection(DatabaseConfig.RootConnectionString);
    rootConnection.Open();

    using (var createDbCommand = new MySqlCommand($"CREATE DATABASE IF NOT EXISTS `{dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;", rootConnection))
    {
        createDbCommand.ExecuteNonQuery();
    }

    using var dbConnection = new MySqlConnection(DatabaseConfig.ConnectionString);
    dbConnection.Open();

    var createUsersTable = @"CREATE TABLE IF NOT EXISTS users (
        id INT AUTO_INCREMENT PRIMARY KEY,
        username VARCHAR(100) NOT NULL UNIQUE,
        password VARCHAR(255) NOT NULL,
        role VARCHAR(50) NOT NULL,
        full_name VARCHAR(150) NOT NULL,
        is_active TINYINT(1) NOT NULL DEFAULT 1
    );";

    using (var cmd = new MySqlCommand(createUsersTable, dbConnection))
    {
        cmd.ExecuteNonQuery();
    }

    var createLogsTable = @"CREATE TABLE IF NOT EXISTS activity_logs (
        id INT AUTO_INCREMENT PRIMARY KEY,
        staff_name VARCHAR(150) NOT NULL,
        action VARCHAR(255) NOT NULL,
        date_occurred DATETIME NOT NULL
    );";

    using (var cmd = new MySqlCommand(createLogsTable, dbConnection))
    {
        cmd.ExecuteNonQuery();
    }

    using (var cmd = new MySqlCommand("SELECT COUNT(*) FROM users WHERE role = 'owner';", dbConnection))
    {
        var ownerCount = Convert.ToInt32(cmd.ExecuteScalar());
        if (ownerCount == 0)
        {
            using var insertOwner = new MySqlCommand("INSERT INTO users (username, password, role, full_name, is_active) VALUES (@user, @pass, 'owner', @name, 1);", dbConnection);
            insertOwner.Parameters.AddWithValue("@user", "admin_crooked");
            insertOwner.Parameters.AddWithValue("@pass", "admin_crooked");
            insertOwner.Parameters.AddWithValue("@name", "Main Owner");
            insertOwner.ExecuteNonQuery();
        }
    }
}
