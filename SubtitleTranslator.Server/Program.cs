using Google.Apis.Auth.OAuth2;
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
// Two auth modes are supported, checked in this order:
//
// 1) GoogleCloud:ApiKey - a plain Cloud Translation API key. Quick to set up
//    for local development/testing, but Google recommends OAuth2 credentials
//    where possible: an API key can't be scoped to a principal or revoked as
//    granularly as a service account key, and it authorizes whoever holds the
//    string, not a specific caller.
//
// 2) GoogleCloud:CredentialsPath - a service account JSON key (the original,
//    recommended path). CredentialsPath (on TranslationClientBuilder) and
//    GoogleCredential.FromFile/FromJson/FromStreamAsync are all deprecated by
//    Google for the same reason: they load an ambiguous credential type from
//    an external source. CredentialFactory is the replacement - it loads the
//    file as the *specific* credential type we expect (a service account
//    key), which is then converted to a GoogleCredential and handed to the
//    builder's non-deprecated GoogleCredential property.
//
// Both values live in appsettings.Local.json, which is gitignored - never
// put a real key or credentials path in appsettings.json/appsettings.Development.json.
var apiKey = builder.Configuration["GoogleCloud:ApiKey"];
TranslationClient translationClient;

if (!string.IsNullOrWhiteSpace(apiKey))
{
    translationClient = TranslationClient.CreateFromApiKey(apiKey);
}
else
{
    var credentialsPath = builder.Configuration["GoogleCloud:CredentialsPath"];
    if (string.IsNullOrWhiteSpace(credentialsPath) || !File.Exists(credentialsPath))
    {
        throw new InvalidOperationException(
            "Set either GoogleCloud:ApiKey or a valid GoogleCloud:CredentialsPath in appsettings.Local.json.");
    }

    var serviceAccountCredential = CredentialFactory.FromFile<ServiceAccountCredential>(credentialsPath);
    var googleCredential = serviceAccountCredential.ToGoogleCredential();

    translationClient = new TranslationClientBuilder
    {
        GoogleCredential = googleCredential
    }.Build();
}

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
