using System.IO;
using Microsoft.Extensions.FileProviders;

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

app.UseCors("AllowAll");
app.UseAuthorization();

var pageFileProvider = new PhysicalFileProvider(
    Path.Combine(builder.Environment.ContentRootPath, "Frontend", "page"));

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(builder.Environment.ContentRootPath, "Frontend", "assets")),
    RequestPath = "/assets"
});

app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = pageFileProvider,
    RequestPath = ""
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = pageFileProvider,
    RequestPath = ""
});

app.MapControllers();

app.Run();