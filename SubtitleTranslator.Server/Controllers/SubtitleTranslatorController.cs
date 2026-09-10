using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SubtitleTranslator.Server.Models;
using SubtitleTranslator.Server.Services;
using System.Text;

namespace SubtitleTranslator.Server.Controllers
{
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
        [HttpPost("translate-and-save")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> TranslateAndSave(
            IFormFile file,
            [FromForm] string targetLanguage,
            CancellationToken cancellationToken)
        {
            if (file is null || file.Length == 0)
                return BadRequest("No subtitle file was provided.");

            if (string.IsNullOrWhiteSpace(targetLanguage))
                return BadRequest("A target language is required.");

            var extension = Path.GetExtension(file.FileName);
            if (!AllowedExtensions.Contains(extension))
                return BadRequest($"Unsupported file type '{extension}'. Only .srt and .vtt files are allowed.");

            if (_settings.MaxFileSizeBytes > 0 && file.Length > _settings.MaxFileSizeBytes)
                return BadRequest($"File exceeds the maximum allowed size of {_settings.MaxFileSizeBytes / (1024 * 1024)} MB.");

            try
            {
                string translatedContent;
                await using (var stream = file.OpenReadStream())
                {
                    translatedContent = await _translationService.TranslateSubtitleAsync(stream, targetLanguage, cancellationToken);
                }

                Directory.CreateDirectory(_settings.OutputPath);

                // Path.GetFileName strips any directory segments the client might smuggle in
                // (e.g. "../../evil.srt"), and the guid avoids collisions/overwrites.
                var safeFileName = Path.GetFileName(file.FileName);
                var outputFileName = $"translated_{targetLanguage}_{Guid.NewGuid():N}_{safeFileName}";
                var destinationPath = Path.Combine(_settings.OutputPath, outputFileName);

                await System.IO.File.WriteAllTextAsync(destinationPath, translatedContent, Encoding.UTF8, cancellationToken);

                return Ok(new { success = true, savedPath = destinationPath });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499); // client closed the request before it finished
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Subtitle translation failed for {FileName}", file.FileName);
                return StatusCode(StatusCodes.Status500InternalServerError, "Translation failed. Please try again.");
            }
        }
    }
}
