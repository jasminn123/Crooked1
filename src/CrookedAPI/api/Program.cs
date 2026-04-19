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

