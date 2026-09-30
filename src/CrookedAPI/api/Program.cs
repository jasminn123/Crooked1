using System.IO;
using Microsoft.Extensions.FileProviders;
using Npgsql;
using Crooked;
using Crooked.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Register threshold forecast options and service
builder.Services.Configure<ThresholdForecastOptions>(builder.Configuration.GetSection("ThresholdForecast"));
builder.Services.AddHostedService<ThresholdForecastService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

EnsureDatabaseSetup();


var pageProvider = new PhysicalFileProvider(
    Path.Combine(builder.Environment.ContentRootPath, "Frontend", "page")
);
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
    FileProvider = new PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "Frontend", "assets")),
    RequestPath = "/assets"
});

var designProvider = new PhysicalFileProvider(
    Path.Combine(builder.Environment.ContentRootPath, "Frontend", "design")
);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = designProvider,
    RequestPath = "/design"
});

var jsProvider = new PhysicalFileProvider(
    Path.Combine(builder.Environment.ContentRootPath, "Frontend", "javascript")
);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = jsProvider,
    RequestPath = "/javascript"
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "Frontend")),
    RequestPath = ""
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
    ),
    RequestPath = ""
});

app.MapControllers();
app.Run();

void EnsureDatabaseSetup()
{
    using var connection = new NpgsqlConnection(DatabaseConfig.ConnectionString);
    connection.Open();

    using var command = new NpgsqlCommand(
        "ALTER TABLE products ADD COLUMN IF NOT EXISTS is_active BOOLEAN NOT NULL DEFAULT TRUE",
        connection);
    command.ExecuteNonQuery();
}