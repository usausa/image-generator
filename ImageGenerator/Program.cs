using ImageGenerator.Components;
using ImageGenerator.Services;
using ImageGenerator.Settings;

using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<ImageGeneratorSetting>(
    builder.Configuration.GetSection("ImageGenerator"));

builder.Services.AddHttpClient(nameof(ImageGeneratorService), client =>
{
    client.Timeout = Timeout.InfiniteTimeSpan;
});

builder.Services.AddSingleton<ImageGeneratorService>();

var app = builder.Build();

// Ensure upload and output directories exist, then serve them as static files.
var setting = app.Configuration.GetSection("ImageGenerator").Get<ImageGeneratorSetting>() ?? new();
var uploadDir = Path.GetFullPath(setting.UploadPath);
var outputDir = Path.GetFullPath(setting.OutputPath);
Directory.CreateDirectory(uploadDir);
Directory.CreateDirectory(outputDir);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadDir),
    RequestPath = "/uploads"
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(outputDir),
    RequestPath = "/output"
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
