var builder = WebApplication.CreateBuilder(args);

// Local development secrets (connection strings, API keys, JWT key, etc.)
// This file is git-ignored - see .gitignore - and lives only on this machine.
// It's optional so the app still starts fine if it doesn't exist yet.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Add services to the container.
// Allowed origins are configured per environment (see appsettings.*.json).
// The SPA is served same-origin by this app (MapFallbackToFile below), so CORS
// is only needed for external clients calling the API directly - keep the list explicit.
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();
app.UseCors();

app.UseDefaultFiles();
app.MapStaticAssets();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(
        options =>
        {
            options.SwaggerEndpoint("../openapi/v1.json", "version 1");
        });
}
app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("/index.html");

app.Run();
