using Microsoft.AspNetCore.Mvc;
using Google.Cloud.Translation.V2;
using Google.Apis.Auth.OAuth2;
using System.Text;
using System.Text.RegularExpressions;

namespace SubtitleTranslator.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SubtitleController : ControllerBase
    {
        private readonly string _storagePath = @"d:\medias\closecaption";
        private readonly string _outputPath = @"d:\medias\closecaption\translate";
        private readonly IConfiguration _configuration;

        public SubtitleController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // 1. GET: api/subtitle/files
        [HttpGet("files")]
        public IActionResult GetAvailableFiles()
        {
            if (!Directory.Exists(_storagePath)) return Ok(new List<object>());

            var files = Directory.GetFiles(_storagePath)
                .Where(f => f.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase))
                .Select(f => new
                {
                    name = Path.GetFileName(f),
                    type = Path.GetExtension(f).Replace(".", "").ToLower()
                })
                .ToList();

            return Ok(files);
        }

        // 2. POST: api/subtitle/translate-and-save
        [HttpPost("translate-and-save")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> TranslateAndSave(IFormFile file, [FromForm] string targetLanguage)
        {
            if (file == null || file.Length == 0) return BadRequest("No subtitle context provided.");

            string credentialsPath = _configuration["GoogleCloud:CredentialsPath"] ?? "";

            if (string.IsNullOrEmpty(credentialsPath) || !System.IO.File.Exists(credentialsPath))
            {
                return StatusCode(500, "Google Cloud CredentialsPath configuration is invalid or missing inside appsettings.");
            }

            // Clean, non-deprecated credential parsing pipeline
            GoogleCredential credential;
            using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
            {
                credential = await GoogleCredential.FromStreamAsync(stream, default);
            }
            var client = TranslationClient.Create(credential);

            var rawFileLines = new List<string>();
            var blocksToTranslate = new List<string>();
            var trackingMap = new Dictionary<int, List<int>>();

            using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
            {
                string? currentLine;
                int fileLinePointer = 0;
                var workingBlockIndices = new List<int>();
                var workingBlockText = new List<string>();

                while ((currentLine = await reader.ReadLineAsync()) != null)
                {
                    rawFileLines.Add(currentLine);

                    bool isTimestamp = Regex.IsMatch(currentLine, @"\d{2}:\d{2}:\d{2}");
                    bool isNumericIndex = int.TryParse(currentLine.Trim(), out _);
                    bool isVttHeader = currentLine.StartsWith("WEBVTT");

                    if (isTimestamp || isNumericIndex || isVttHeader || string.IsNullOrWhiteSpace(currentLine))
                    {
                        if (workingBlockText.Count > 0)
                        {
                            PackageSubtitleBlock(workingBlockText, workingBlockIndices, blocksToTranslate, trackingMap);
                            workingBlockText.Clear();
                            workingBlockIndices.Clear();
                        }
                    }
                    else
                    {
                        workingBlockText.Add(currentLine);
                        workingBlockIndices.Add(fileLinePointer);
                    }
                    fileLinePointer++;
                }

                if (workingBlockText.Count > 0)
                {
                    PackageSubtitleBlock(workingBlockText, workingBlockIndices, blocksToTranslate, trackingMap);
                }
            }

            if (blocksToTranslate.Count > 0)
            {
                var translationResponses = new List<TranslationResult>();
                int chunkSize = 500;

                for (int i = 0; i < blocksToTranslate.Count; i += chunkSize)
                {
                    var chunk = blocksToTranslate.Skip(i).Take(chunkSize).ToList();

                    // FIXED: Changed 'Format.Html' to use explicit mapping 'TranslationFormat.Html'
                    var chunkResponse = client.TranslateText(chunk, targetLanguage, null, TranslationModel.Base, TranslationFormat.Html);
                    translationResponses.AddRange(chunkResponse);
                }

                for (int i = 0; i < translationResponses.Count; i++)
                {
                    string returnedHtml = translationResponses[i].TranslatedText;
                    var innerTagMatches = Regex.Matches(returnedHtml, @"<div>(.*?)<\/div>");
                    var originalLineIndices = trackingMap[i];

                    for (int tagIdx = 0; tagIdx < innerTagMatches.Count; tagIdx++)
                    {
                        if (tagIdx < originalLineIndices.Count)
                        {
                            int targetedOriginalLine = originalLineIndices[tagIdx];

                            // FIXED: Targeted specific capture inner group [1] and called its .Value property cleanly
                            string targetText = innerTagMatches[tagIdx].Groups[1].Value;

                            rawFileLines[targetedOriginalLine] = System.Net.WebUtility.HtmlDecode(targetText);
                        }
                    }
                }
            }

            var finalStringBuilder = new StringBuilder();
            foreach (var constructedLine in rawFileLines)
            {
                finalStringBuilder.AppendLine(constructedLine);
            }

            string cleanFileName = Path.GetFileName(file.FileName);
            string destinationPath = Path.Combine(_outputPath, $"translated_{targetLanguage}_{cleanFileName}");

            await System.IO.File.WriteAllTextAsync(destinationPath, finalStringBuilder.ToString(), Encoding.UTF8);

            return Ok(new { success = true, savedPath = destinationPath });
        }

        private void PackageSubtitleBlock(List<string> dialogueLines, List<int> linePointers, List<string> batchContainer, Dictionary<int, List<int>> mappingRegistry)
        {
            var htmlSegmentBuilder = new StringBuilder();
            foreach (var lineItem in dialogueLines)
            {
                htmlSegmentBuilder.Append($"<div>{System.Net.WebUtility.HtmlEncode(lineItem)}</div>");
            }
            int targetBatchIdx = batchContainer.Count;
            batchContainer.Add(htmlSegmentBuilder.ToString());
            mappingRegistry[targetBatchIdx] = new List<int>(linePointers);
        }
    }
}

