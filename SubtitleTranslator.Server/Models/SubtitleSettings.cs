namespace SubtitleTranslator.Server.Models
{
    /// <summary>
    /// Bind this to the "Subtitle" section in appsettings.json / appsettings.Local.json.
    /// Keeping paths in configuration (instead of hardcoded) means they can differ
    /// per machine/environment (Windows dev box, Linux container, CI, etc.).
    /// </summary>
    public class SubtitleSettings
    {
        public string StoragePath { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;

        /// <summary>Set to 0 to disable the size check.</summary>
        public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024; // 10 MB default
    }
}
