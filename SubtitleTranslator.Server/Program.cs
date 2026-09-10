using Google.Cloud.Translation.V2;
using Microsoft.Extensions.Options;
using SubtitleTranslator.Server.Models;
using SubtitleTranslator.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Load the local configuration file during development
if (builder.Environment.IsDevelopment())
{
    // NOTE: capitalized to match appsettings.Local.json.example in the repo.
    // "appsettings.local.json" (lowercase l) and "appsettings.Local.json" are
    // two different files on case-sensitive filesystems (Linux/macOS/most CI),
    // even though Windows hides the difference.
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
}
const string AngularCorsPolicy = "_angularCorsPolicy";

var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>();
if (allowedOrigins is null || allowedOrigins.Length == 0)
{
    allowedOrigins = ["http://localhost:4200"];
}

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: AngularCorsPolicy,
        policy =>
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .WithExposedHeaders("Content-Disposition");
        });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Subtitle feature: paths/limits come from configuration (appsettings.json /
// appsettings.Local.json) instead of being hardcoded, so this works outside
// of one specific machine.
builder.Services.Configure<SubtitleSettings>(builder.Configuration.GetSection("Subtitle"));

// The client is built once, here at startup - not on every request - since
// credential loading is comparatively expensive and the client itself is safe
// to share across requests.
//
// Auth is a plain Cloud Translation API key (GoogleCloud:ApiKey), which lives
// in appsettings.Local.json - gitignored, never committed. Note this is
// simpler than a service-account key but less secure long-term: an API key
// authorizes whoever holds the string rather than a specific principal, and
// can't be scoped/revoked as granularly. Restrict it in Google Cloud Console
// (limit it to the Cloud Translation API, and to specific IPs if possible)
// if this ever runs anywhere other than your own machine.
var apiKey = builder.Configuration["GoogleCloud:ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException(
        "GoogleCloud:ApiKey is missing. Set it in appsettings.Local.json.");
}

var translationClient = TranslationClient.CreateFromApiKey(apiKey);

builder.Services.AddSingleton(translationClient);

builder.Services.AddScoped<ISubtitleTranslationService, SubtitleTranslationService>();

var app = builder.Build();

// Ensure the configured subtitle storage/output folders exist on launch.
// Falls back to the original hardcoded defaults if "Subtitle" isn't configured,
// so behavior is unchanged until you set StoragePath/OutputPath explicitly.
var subtitleSettings = app.Services.GetRequiredService<IOptions<SubtitleSettings>>().Value;
string baseFolder = string.IsNullOrWhiteSpace(subtitleSettings.StoragePath)
    ? @"d:\medias\closecaption"
    : subtitleSettings.StoragePath;
string translateFolder = string.IsNullOrWhiteSpace(subtitleSettings.OutputPath)
    ? Path.Combine(baseFolder, "translate")
    : subtitleSettings.OutputPath;

if (!Directory.Exists(baseFolder)) Directory.CreateDirectory(baseFolder);
if (!Directory.Exists(translateFolder)) Directory.CreateDirectory(translateFolder);

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors(AngularCorsPolicy);
app.UseAuthorization();
app.MapControllers();

app.Run();
