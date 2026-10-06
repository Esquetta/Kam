using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Removes what speech recognition writes for silence and noise: tags such as <c>[Music]</c> and the subtitle
/// credits Whisper is known to invent, in English and Turkish.
/// </summary>
public static partial class TranscriptCleaner
{
    /// <summary>
    /// The error a speech engine returns when it worked but heard no words.
    /// </summary>
    public const string NoSpeechMessage = "No speech was recognized.";

    private static readonly string[] s_inventedLines =
    [
        "altyazı m.k.",
        "altyazı m.k",
        "altyazı",
        "izlediğiniz için teşekkürler",
        "izlediğiniz için teşekkür ederim",
        "abone olmayı unutmayın",
        "thanks for watching",
        "thank you for watching",
        "subtitles by the amara.org community",
        "you"
    ];

    /// <summary>
    /// Returns the spoken text without tags and invented lines; empty when nothing was said.
    /// </summary>
    /// <param name="text">The raw transcript.</param>
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleaned = Tag().Replace(text, " ");
        cleaned = Spaces().Replace(cleaned, " ").Trim();
        var bare = cleaned.Trim(' ', '.', '!', '?', ',', '…', '-', '"').ToLowerInvariant();
        return bare.Length == 0 || s_inventedLines.Contains(bare) ? string.Empty : cleaned;
    }

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*|♪+")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
