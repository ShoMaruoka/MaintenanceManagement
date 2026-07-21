using System.Text;
using MaintenanceManagement.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.IIS;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = ImagePrepareService.MaxUploadBytes;
});
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = ImagePrepareService.MaxUploadBytes;
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = ImagePrepareService.MaxUploadBytes;
});

builder.Services.AddAuthentication(Microsoft.AspNetCore.Server.IIS.IISServerDefaults.AuthenticationScheme);

builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddScoped<ModuleQueryService>();
builder.Services.AddScoped<DeployService>();
builder.Services.AddScoped<FastCopyService>();
builder.Services.AddScoped<ImagePrepareService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"])
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapFallbackToFile("index.html");

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<DatabaseService>().EnsureCreated();
}

app.Run();
