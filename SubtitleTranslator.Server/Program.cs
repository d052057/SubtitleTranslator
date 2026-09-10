var builder = WebApplication.CreateBuilder(args);

// Load the local configuration file during development
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);
}

const string AngularCorsPolicy = "_angularCorsPolicy";

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: AngularCorsPolicy,
        policy =>
        {
            policy.WithOrigins("http://localhost:4200")
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .WithExposedHeaders("Content-Disposition");
        });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Auto-ensure physical folder workspaces exist on launch
string baseFolder = @"d:\medias\closecaption";
string translateFolder = Path.Combine(baseFolder, "translate");
if (!Directory.Exists(baseFolder)) Directory.CreateDirectory(baseFolder);
if (!Directory.Exists(translateFolder)) Directory.CreateDirectory(translateFolder);

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors(AngularCorsPolicy);
app.UseAuthorization();
app.MapControllers();

app.Run();
