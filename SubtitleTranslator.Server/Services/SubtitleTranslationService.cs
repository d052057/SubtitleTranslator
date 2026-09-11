using Google.Cloud.Translation.V2;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace SubtitleTranslator.Server.Services
{
    /// <summary>
    /// The result of translating a subtitle file: the reconstructed file content, plus
    /// the source language Google Translate detected (null if it couldn't tell, e.g. no
    /// translatable dialogue lines were found).
    /// </summary>
    public record SubtitleTranslationResult(string Content, string? DetectedSourceLanguage);

    public interface ISubtitleTranslationService
    {
        /// <summary>
        /// Reads an SRT/VTT stream, translates the dialogue lines, and returns the
        /// reconstructed file content with translated text in place of the originals.
        /// </summary>
        Task<SubtitleTranslationResult> TranslateSubtitleAsync(Stream subtitleStream, string targetLanguage, CancellationToken cancellationToken = default);

        /// <summary>
        /// True if targetLanguage is one Google Cloud Translation currently supports.
        /// The underlying list is fetched once and cached for the app's lifetime.
        /// </summary>
        Task<bool> IsSupportedLanguageAsync(string targetLanguage, CancellationToken cancellationToken = default);
    }

    public class SubtitleTranslationService : ISubtitleTranslationService
    {
        // Google Cloud Translation API v2's REST endpoint hard-caps requests at 128 text
        // segments (https://cloud.google.com/translate/docs/basic/translating-text) -
        // going over returns a 400 "Too many text segments" error, not a soft warning.
        // Chunking keeps every request under that limit regardless of how many cues the
        // subtitle file has.
        private const int ChunkSize = 128;

        private static readonly Regex TimestampPattern = new(@"\d{2}:\d{2}:\d{2}", RegexOptions.Compiled);
        private static readonly Regex HtmlDivPattern = new(@"<div>(.*?)</div>", RegexOptions.Compiled);

        private readonly TranslationClient _translationClient;
        private readonly ILogger<SubtitleTranslationService> _logger;

        // Lazily populated on first use and reused for the app's lifetime - the list of
        // supported languages doesn't change during a run, and fetching it is a network
        // call we don't want to repeat on every request.
        private readonly SemaphoreSlim _supportedLanguagesLock = new(1, 1);
        private HashSet<string>? _supportedLanguageCodes;

        public SubtitleTranslationService(TranslationClient translationClient, ILogger<SubtitleTranslationService> logger)
        {
            _translationClient = translationClient;
            _logger = logger;
        }

        public async Task<bool> IsSupportedLanguageAsync(string targetLanguage, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(targetLanguage)) return false;

            if (_supportedLanguageCodes is null)
            {
                await _supportedLanguagesLock.WaitAsync(cancellationToken);
                try
                {
                    // Double-checked locking: another request may have populated this
                    // while we were waiting for the lock.
                    if (_supportedLanguageCodes is null)
                    {
                        var languages = await _translationClient.ListLanguagesAsync(
                            LanguageCodes.English, cancellationToken: cancellationToken);

                        _supportedLanguageCodes = new HashSet<string>(
                            languages.Select(l => l.Code), StringComparer.OrdinalIgnoreCase);
                    }
                }
                finally
                {
                    _supportedLanguagesLock.Release();
                }
            }

            return _supportedLanguageCodes.Contains(targetLanguage);
        }

        public async Task<SubtitleTranslationResult> TranslateSubtitleAsync(Stream subtitleStream, string targetLanguage, CancellationToken cancellationToken = default)
        {
            var (rawLines, blocks, lineMap) = await ParseSubtitleAsync(subtitleStream, cancellationToken);

            if (blocks.Count == 0)
            {
                _logger.LogWarning("No translatable dialogue lines were found in the uploaded subtitle file.");
                return new SubtitleTranslationResult(string.Join(Environment.NewLine, rawLines), DetectedSourceLanguage: null);
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

            // All blocks come from the same file, so the first detected language is a
            // reasonable stand-in for "the file's language" even though each block was
            // detected independently - genuinely mixed-language files are rare enough
            // not to warrant surfacing every distinct detection.
            var detectedSourceLanguage = translations.Count > 0 ? translations[0].DetectedSourceLanguage : null;

            return new SubtitleTranslationResult(string.Join(Environment.NewLine, rawLines), detectedSourceLanguage);
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
