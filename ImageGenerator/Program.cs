using ImageGenerator.Components;
using ImageGenerator.Services;
using ImageGenerator.Settings;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<ImageGeneratorSetting>(
    builder.Configuration.GetSection("ImageGenerator"));

builder.Services.AddHttpClient(nameof(ImageGeneratorService), client =>
{
    client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
});

builder.Services.AddSingleton<ImageGeneratorService>();

var app = builder.Build();

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
