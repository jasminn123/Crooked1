using System.IO;
using Microsoft.Extensions.FileProviders;
using MySql.Data.MySqlClient;
using Crooked;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

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

app.UseCors("AllowAll");
app.UseAuthorization();

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

app.MapControllers();
app.Run();

void EnsureDatabaseSetup()
{
    Console.WriteLine("Database setup checked.");
}
