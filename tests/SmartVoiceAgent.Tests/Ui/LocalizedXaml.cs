using SmartVoiceAgent.Ui.Services;
using System.Security;
using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Tests.Ui;

/// <summary>
/// Reads view markup with its <c>{DynamicResource Lang.*}</c> text replaced by the English copy, so metadata
/// tests pin what a person reads rather than resource keys.
/// </summary>
internal static partial class LocalizedXaml
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> s_english =
        new(() => LocalizationService.LoadDictionary(LocalizationService.DefaultLanguage));

    /// <summary>
    /// Gets the English interface text by key.
    /// </summary>
    public static IReadOnlyDictionary<string, string> English => s_english.Value;

    /// <summary>
    /// Reads a markup file and resolves its language resources to English.
    /// </summary>
    /// <param name="path">The markup file.</param>
    public static string ReadAllText(string path) => Resolve(File.ReadAllText(path));

    /// <summary>
    /// Replaces every <c>{DynamicResource Lang.Key}</c> with the XML-escaped English text for that key.
    /// </summary>
    /// <param name="markup">The markup.</param>
    public static string Resolve(string markup)
    {
        return LanguageResource().Replace(markup, match =>
            English.TryGetValue(match.Groups["key"].Value, out var text)
                ? SecurityElement.Escape(text)
                : match.Value);
    }

    [GeneratedRegex(@"\{DynamicResource\s+Lang\.(?<key>[A-Za-z0-9_.]+)\}")]
    private static partial Regex LanguageResource();
}
