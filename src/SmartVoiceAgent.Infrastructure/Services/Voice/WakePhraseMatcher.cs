using System.Globalization;
using System.Text;

namespace SmartVoiceAgent.Infrastructure.Services.Voice;

/// <summary>
/// The result of looking for the wake phrase in a transcript.
/// </summary>
/// <param name="IsMatch">Whether the transcript starts with the wake phrase.</param>
/// <param name="Score">How close the closest words were, 0..1.</param>
/// <param name="FollowingText">The words after the wake phrase, as they were written.</param>
public readonly record struct WakePhraseMatch(bool IsMatch, double Score, string FollowingText);

/// <summary>
/// Decides whether a transcript starts with the wake phrase, forgiving how speech recognition spells it
/// ("Hey Kam", "hey cam", "Hey, Kam!", "heykam") and Turkish letters.
/// </summary>
public static class WakePhraseMatcher
{
    /// <summary>
    /// The similarity a transcript needs at the default sensitivity.
    /// </summary>
    public const double DefaultThreshold = 0.7;

    private const int MaxLeadingWords = 2;

    /// <summary>
    /// Looks for <paramref name="wakePhrase"/> at the start of <paramref name="transcript"/>.
    /// </summary>
    /// <param name="transcript">What speech recognition heard.</param>
    /// <param name="wakePhrase">The wake phrase.</param>
    /// <param name="threshold">The similarity needed, 0..1.</param>
    public static WakePhraseMatch Match(string? transcript, string? wakePhrase, double threshold = DefaultThreshold)
    {
        var phrase = string.Concat(Words(wakePhrase ?? string.Empty).Select(word => word.Key));
        if (phrase.Length == 0 || string.IsNullOrWhiteSpace(transcript))
        {
            return new WakePhraseMatch(false, 0, string.Empty);
        }

        var words = Words(transcript).ToList();
        var phraseWords = Math.Max(1, Words(wakePhrase!).Count());
        var best = new WakePhraseMatch(false, 0, string.Empty);

        for (var start = 0; start <= Math.Min(MaxLeadingWords, words.Count - 1); start++)
        {
            for (var length = Math.Max(1, phraseWords - 1); length <= phraseWords + 1 && start + length <= words.Count; length++)
            {
                var candidate = string.Concat(words.Skip(start).Take(length).Select(word => word.Key));
                var score = Similarity(candidate, phrase);
                if (score > best.Score)
                {
                    var following = start + length < words.Count
                        ? transcript[words[start + length].Start..].Trim().TrimStart(',', '.', '!', '?', ';', ':', '-', '…').Trim()
                        : string.Empty;
                    best = new WakePhraseMatch(score >= threshold, score, following);
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Returns the matching key of a word: lowercase, no accents, Turkish dotless and dotted i folded, and
    /// letters that sound alike (c, k, q) folded together.
    /// </summary>
    /// <param name="word">The word.</param>
    public static string Key(string word)
    {
        var builder = new StringBuilder(word.Length);
        foreach (var character in word.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = character switch
            {
                'I' or 'İ' or 'ı' => 'i',
                _ => char.ToLowerInvariant(character)
            };

            if (!char.IsLetterOrDigit(lower))
            {
                continue;
            }

            lower = lower is 'c' or 'q' ? 'k' : lower;
            if (builder.Length == 0 || builder[^1] != lower)
            {
                builder.Append(lower);
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<(string Key, int Start)> Words(string text)
    {
        var index = 0;
        foreach (var part in text.Split((char[]?)null, StringSplitOptions.None))
        {
            var start = text.IndexOf(part, index, StringComparison.Ordinal);
            index = start + part.Length;
            if (part.Length == 0)
            {
                continue;
            }

            var key = Key(part);
            if (key.Length > 0)
            {
                yield return (key, start);
            }
        }
    }

    private static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return 1 - previous[b.Length] / (double)Math.Max(a.Length, b.Length);
    }
}
