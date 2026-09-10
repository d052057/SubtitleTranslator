using Google.Cloud.Translation.V2;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace SubtitleTranslator.Server.Services
{
    public interface ISubtitleTranslationService
    {
        /// <summary>
        /// Reads an SRT/VTT stream, translates the dialogue lines, and returns the
        /// reconstructed file content with translated text in place of the originals.
        /// </summary>
        Task<string> TranslateSubtitleAsync(Stream subtitleStream, string targetLanguage, CancellationToken cancellationToken = default);
    }

    public class SubtitleTranslationService : ISubtitleTranslationService
    {
        // Google Translate's batch endpoint has practical limits on request size;
        // chunking keeps individual calls well within them.
        private const int ChunkSize = 500;

        private static readonly Regex TimestampPattern = new(@"\d{2}:\d{2}:\d{2}", RegexOptions.Compiled);
        private static readonly Regex HtmlDivPattern = new(@"<div>(.*?)</div>", RegexOptions.Compiled);

        private readonly TranslationClient _translationClient;
        private readonly ILogger<SubtitleTranslationService> _logger;

        public SubtitleTranslationService(TranslationClient translationClient, ILogger<SubtitleTranslationService> logger)
        {
            _translationClient = translationClient;
            _logger = logger;
        }

        public async Task<string> TranslateSubtitleAsync(Stream subtitleStream, string targetLanguage, CancellationToken cancellationToken = default)
        {
            var (rawLines, blocks, lineMap) = await ParseSubtitleAsync(subtitleStream, cancellationToken);

            if (blocks.Count == 0)
            {
                _logger.LogWarning("No translatable dialogue lines were found in the uploaded subtitle file.");
                return string.Join(Environment.NewLine, rawLines);
            }

            var translations = new List<TranslationResult>(blocks.Count);

            for (int i = 0; i < blocks.Count; i += ChunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chunk = blocks.Skip(i).Take(ChunkSize).ToList();

                var chunkResult = await _translationClient.TranslateHtmlAsync(
                    chunk,
                    targetLanguage,
                    sourceLanguage: null,
                    model: TranslationModel.Base,
                    cancellationToken: cancellationToken);

                translations.AddRange(chunkResult);
            }

            ApplyTranslations(rawLines, translations, lineMap);

            return string.Join(Environment.NewLine, rawLines);
        }

        private static async Task<(List<string> RawLines, List<string> Blocks, Dictionary<int, List<int>> LineMap)> ParseSubtitleAsync(
            Stream stream, CancellationToken cancellationToken)
        {
            var rawLines = new List<string>();
            var blocks = new List<string>();
            var lineMap = new Dictionary<int, List<int>>();

            var workingLines = new List<string>();
            var workingIndices = new List<int>();

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
            string? line;
            int lineNumber = 0;

            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                rawLines.Add(line);

                bool isBoundary = TimestampPattern.IsMatch(line)
                    || int.TryParse(line.Trim(), out _)
                    || line.StartsWith("WEBVTT", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(line);

                if (isBoundary)
                {
                    FlushBlock(workingLines, workingIndices, blocks, lineMap);
                }
                else
                {
                    workingLines.Add(line);
                    workingIndices.Add(lineNumber);
                }

                lineNumber++;
            }

            FlushBlock(workingLines, workingIndices, blocks, lineMap);

            return (rawLines, blocks, lineMap);
        }

        private static void FlushBlock(
            List<string> workingLines, List<int> workingIndices,
            List<string> blocks, Dictionary<int, List<int>> lineMap)
        {
            if (workingLines.Count == 0) return;

            var html = new StringBuilder();
            foreach (var text in workingLines)
                html.Append("<div>").Append(System.Net.WebUtility.HtmlEncode(text)).Append("</div>");

            lineMap[blocks.Count] = new List<int>(workingIndices);
            blocks.Add(html.ToString());

            workingLines.Clear();
            workingIndices.Clear();
        }

        private static void ApplyTranslations(
            List<string> rawLines, List<TranslationResult> translations, Dictionary<int, List<int>> lineMap)
        {
            for (int i = 0; i < translations.Count; i++)
            {
                if (!lineMap.TryGetValue(i, out var originalIndices)) continue;

                var matches = HtmlDivPattern.Matches(translations[i].TranslatedText);

                for (int m = 0; m < matches.Count && m < originalIndices.Count; m++)
                {
                    rawLines[originalIndices[m]] = System.Net.WebUtility.HtmlDecode(matches[m].Groups[1].Value);
                }
            }
        }
    }
}
