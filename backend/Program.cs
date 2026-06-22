using System.Text;
using MaintenanceManagement.Api.Services;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddAuthentication(Microsoft.AspNetCore.Server.IIS.IISServerDefaults.AuthenticationScheme);

builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddScoped<ModuleQueryService>();
builder.Services.AddScoped<DeployService>();
builder.Services.AddScoped<FastCopyService>();

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
