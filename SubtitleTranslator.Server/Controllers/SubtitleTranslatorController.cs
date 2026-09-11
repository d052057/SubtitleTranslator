using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SubtitleTranslator.Server.Models;
using SubtitleTranslator.Server.Services;
using System.Text;

namespace SubtitleTranslator.Server.Controllers
{
    public record TranslateServerFileRequest(string FileName, string TargetLanguage);

    [ApiController]
    [Route("api/[controller]")]
    public class SubtitleController : ControllerBase
    {
        private static readonly HashSet<string> AllowedExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".srt", ".vtt" };

        private readonly SubtitleSettings _settings;
        private readonly ISubtitleTranslationService _translationService;
        private readonly ILogger<SubtitleController> _logger;

        public SubtitleController(
            IOptions<SubtitleSettings> settings,
            ISubtitleTranslationService translationService,
            ILogger<SubtitleController> logger)
        {
            _settings = settings.Value;
            _translationService = translationService;
            _logger = logger;
        }

        // GET: api/subtitle/files
        [HttpGet("files")]
        public IActionResult GetAvailableFiles()
        {
            if (string.IsNullOrWhiteSpace(_settings.StoragePath) || !Directory.Exists(_settings.StoragePath))
                return Ok(Array.Empty<object>());

            try
            {
                var files = Directory.EnumerateFiles(_settings.StoragePath)
                    .Where(f => AllowedExtensions.Contains(Path.GetExtension(f)))
                    .Select(f => new
                    {
                        name = Path.GetFileName(f),
                        type = Path.GetExtension(f).TrimStart('.').ToLowerInvariant()
                    })
                    .ToList();

                return Ok(files);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Failed to list subtitle files in {StoragePath}", _settings.StoragePath);
                return StatusCode(StatusCodes.Status500InternalServerError, "Unable to read the subtitle storage directory.");
            }
        }

        // POST: api/subtitle/translate-and-save
        // Translates a file the user uploaded/dropped from their own machine.
        [HttpPost("translate-and-save")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> TranslateAndSave(
            IFormFile file,
            [FromForm] string targetLanguage,
            CancellationToken cancellationToken)
        {
            if (file is null || file.Length == 0)
                return BadRequest("No subtitle file was provided.");

            var extension = Path.GetExtension(file.FileName);
            if (!AllowedExtensions.Contains(extension))
                return BadRequest($"Unsupported file type '{extension}'. Only .srt and .vtt files are allowed.");

            if (_settings.MaxFileSizeBytes > 0 && file.Length > _settings.MaxFileSizeBytes)
                return BadRequest($"File exceeds the maximum allowed size of {_settings.MaxFileSizeBytes / (1024 * 1024)} MB.");

            var languageError = await ValidateTargetLanguageAsync(targetLanguage, cancellationToken);
            if (languageError is not null) return languageError;

            await using var stream = file.OpenReadStream();
            return await TranslateAndSaveInternalAsync(stream, file.FileName, targetLanguage, cancellationToken);
        }

        // POST: api/subtitle/translate-server-file
        // Translates a file that's already sitting in StoragePath - the server-side
        // equivalent of translate-and-save, for the "Source Folder" list in the UI.
        // No upload involved: we open the file directly by name.
        [HttpPost("translate-server-file")]
        public async Task<IActionResult> TranslateServerFile(
            [FromBody] TranslateServerFileRequest request,
            CancellationToken cancellationToken)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.FileName))
                return BadRequest("A server file name is required.");

            var extension = Path.GetExtension(request.FileName);
            if (!AllowedExtensions.Contains(extension))
                return BadRequest($"Unsupported file type '{extension}'. Only .srt and .vtt files are allowed.");

            var languageError = await ValidateTargetLanguageAsync(request.TargetLanguage, cancellationToken);
            if (languageError is not null) return languageError;

            // Path.GetFileName strips any directory segments the client might smuggle in
            // (e.g. "../../../windows/system.ini") before it ever touches the filesystem.
            var safeFileName = Path.GetFileName(request.FileName);
            var sourcePath = Path.Combine(_settings.StoragePath, safeFileName);

            // Guards against a filename that, even after GetFileName, could still resolve
            // outside StoragePath on some filesystems/edge cases - belt and suspenders
            // alongside the GetFileName call above.
            var fullStoragePath = Path.GetFullPath(_settings.StoragePath);
            var fullSourcePath = Path.GetFullPath(sourcePath);
            if (!fullSourcePath.StartsWith(fullStoragePath, StringComparison.OrdinalIgnoreCase))
                return BadRequest("Invalid file name.");

            if (!System.IO.File.Exists(fullSourcePath))
                return NotFound($"'{safeFileName}' was not found in the source folder.");

            await using var stream = System.IO.File.OpenRead(fullSourcePath);
            return await TranslateAndSaveInternalAsync(stream, safeFileName, request.TargetLanguage, cancellationToken);
        }

        private async Task<IActionResult?> ValidateTargetLanguageAsync(string targetLanguage, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(targetLanguage))
                return BadRequest("A target language is required.");

            if (!await _translationService.IsSupportedLanguageAsync(targetLanguage, cancellationToken))
                return BadRequest($"'{targetLanguage}' is not a language Google Translate currently supports.");

            return null;
        }

        private async Task<IActionResult> TranslateAndSaveInternalAsync(
            Stream sourceStream, string originalFileName, string targetLanguage, CancellationToken cancellationToken)
        {
            try
            {
                var translationResult = await _translationService.TranslateSubtitleAsync(sourceStream, targetLanguage, cancellationToken);

                Directory.CreateDirectory(_settings.OutputPath);

                var safeFileName = Path.GetFileName(originalFileName);
                var outputFileName = $"translated_{targetLanguage}_{Guid.NewGuid():N}_{safeFileName}";
                var destinationPath = Path.Combine(_settings.OutputPath, outputFileName);

                await System.IO.File.WriteAllTextAsync(destinationPath, translationResult.Content, Encoding.UTF8, cancellationToken);

                return Ok(new
                {
                    success = true,
                    savedPath = destinationPath,
                    detectedSourceLanguage = translationResult.DetectedSourceLanguage
                });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499); // client closed the request before it finished
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Subtitle translation failed for {FileName}", originalFileName);
                return StatusCode(StatusCodes.Status500InternalServerError, "Translation failed. Please try again.");
            }
        }
    }
}
