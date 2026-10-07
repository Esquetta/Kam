using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Infrastructure.Services.Speech;

/// <summary>
/// Turns an agent reply written in Markdown into text that sounds right read aloud: no code blocks, tables,
/// link targets, list markers or emphasis characters, cut at a sentence boundary when it is long.
/// </summary>
public static partial class SpeechTextFormatter
{
    /// <summary>
    /// The longest text read aloud, in characters.
    /// </summary>
    public const int DefaultMaxLength = 600;

    /// <summary>
    /// Returns speakable text for a Markdown reply; empty when nothing in it is worth reading.
    /// </summary>
    /// <param name="markdown">The reply.</param>
    /// <param name="maxLength">The longest text returned.</param>
    public static string Format(string? markdown, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var text = CodeFence().Replace(markdown, " ");
        var lines = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('|') || HorizontalRule().IsMatch(line))
            {
                continue;
            }

            line = Heading().Replace(line, string.Empty);
            line = ListMarker().Replace(line, string.Empty);
            line = TaskBox().Replace(line, string.Empty);
            line = Quote().Replace(line, string.Empty);
            lines.Add(EndSentence(line));
        }

        text = string.Join(" ", lines);
        text = Image().Replace(text, "$1");
        text = Link().Replace(text, "$1");
        text = BareUrl().Replace(text, string.Empty);
        text = InlineCode().Replace(text, "$1");
        text = Emphasis().Replace(text, "$2");
        text = text.Replace("&nbsp;", " ", StringComparison.Ordinal);
        text = Spaces().Replace(text, " ").Trim();
        return Shorten(text, maxLength);
    }

    /// <summary>
    /// Splits text into sentences, so reading can start before the whole reply is synthesized.
    /// </summary>
    /// <param name="text">Plain text.</param>
    public static IReadOnlyList<string> SplitSentences(string text)
    {
        return SentenceEnd().Split(text)
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Returns the two-letter language of <paramref name="text"/>: <c>tr</c> when it has Turkish letters, else <c>en</c>.
    /// </summary>
    /// <param name="text">The text.</param>
    public static string GuessLanguage(string text)
    {
        return text.AsSpan().IndexOfAny("çğıöşüÇĞİÖŞÜ") >= 0 ? "tr" : "en";
    }

    private static string EndSentence(string line)
    {
        return line.Length > 0 && !".!?:;…".Contains(line[^1]) ? line + "." : line;
    }

    private static string Shorten(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var cut = text.LastIndexOfAny(['.', '!', '?'], maxLength - 1);
        return cut >= maxLength / 3 ? text[..(cut + 1)] : text[..maxLength].TrimEnd() + "…";
    }

    [GeneratedRegex(@"```[\s\S]*?(```|$)|~~~[\s\S]*?(~~~|$)")]
    private static partial Regex CodeFence();

    [GeneratedRegex(@"^#{1,6}\s+")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^([-*+]|\d+[.)])\s+")]
    private static partial Regex ListMarker();

    [GeneratedRegex(@"^\[[ xX]\]\s+")]
    private static partial Regex TaskBox();

    [GeneratedRegex(@"^>\s?")]
    private static partial Regex Quote();

    [GeneratedRegex(@"^([-*_]\s*){3,}$")]
    private static partial Regex HorizontalRule();

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"\bhttps?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex BareUrl();

    [GeneratedRegex(@"`([^`]*)`")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"(\*\*|__|\*|_|~~)(\S(?:.*?\S)?)\1")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"(?<=[.!?…])\s+")]
    private static partial Regex SentenceEnd();
}
