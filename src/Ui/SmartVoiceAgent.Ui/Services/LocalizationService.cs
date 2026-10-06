using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// A language the interface can be shown in.
/// </summary>
/// <param name="Code">The culture name, such as <c>tr-TR</c>.</param>
/// <param name="DisplayName">The language's own name, such as <c>Türkçe</c>.</param>
public sealed record LanguageOption(string Code, string DisplayName)
{
    /// <inheritdoc />
    public override string ToString() => DisplayName;
}

/// <summary>
/// Holds the interface text for the selected language. Text lives in <c>Assets/Lang/{code}.{Area}.json</c>,
/// one file per page or area; English is the base, so a key missing from another language shows in English.
/// XAML reads it with <c>{DynamicResource Lang.Key}</c> and code with <see cref="Get"/> or <see cref="Format"/>.
/// </summary>
public sealed class LocalizationService
{
    /// <summary>
    /// The language every other language falls back to.
    /// </summary>
    public const string DefaultLanguage = "en-US";

    /// <summary>
    /// The prefix of the application resource keys XAML binds to.
    /// </summary>
    public const string ResourcePrefix = "Lang.";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> s_english =
        new(() => LoadDictionary(DefaultLanguage));

    private readonly object _gate = new();
    private IReadOnlyDictionary<string, string> _current;
    private ResourceDictionary? _resources;

    /// <summary>
    /// Gets the instance the application uses.
    /// </summary>
    public static LocalizationService Instance { get; } = new();

    /// <summary>
    /// Gets the languages the interface is translated into.
    /// </summary>
    public static IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
    [
        new("en-US", "English"),
        new("tr-TR", "Türkçe")
    ];

    /// <summary>
    /// Creates a service that starts in English. The application uses <see cref="Instance"/>.
    /// </summary>
    public LocalizationService()
    {
        _current = s_english.Value;
    }

    /// <summary>
    /// Raised after the language changes, so code-built text can refresh.
    /// </summary>
    public event EventHandler? LanguageChanged;

    /// <summary>
    /// Gets the culture name of the current language.
    /// </summary>
    public string CurrentLanguage { get; private set; } = DefaultLanguage;

    /// <summary>
    /// Gets the culture used to format dates and numbers in the current language.
    /// </summary>
    public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLanguage);

    /// <summary>
    /// Gets whether the interface is in Turkish.
    /// </summary>
    public bool IsTurkish => CurrentLanguage == "tr-TR";

    /// <summary>
    /// Returns the text for <paramref name="key"/> in the current language, then English, then the key itself.
    /// </summary>
    /// <param name="key">The text key without the <c>Lang.</c> prefix, such as <c>Chat.NewTask</c>.</param>
    public string Get(string key)
    {
        if (_current.TryGetValue(key, out var value) || s_english.Value.TryGetValue(key, out value))
        {
            return value;
        }

        return key;
    }

    /// <summary>
    /// Formats the text for <paramref name="key"/> with <paramref name="args"/> in the current culture.
    /// </summary>
    /// <param name="key">The text key.</param>
    /// <param name="args">The values for the text's <c>{0}</c> style placeholders.</param>
    public string Format(string key, params object?[] args)
    {
        return string.Format(Culture, Get(key), args);
    }

    /// <summary>
    /// Switches the interface language. Unknown codes fall back to English.
    /// </summary>
    /// <param name="languageCode">A culture name such as <c>tr-TR</c> or <c>tr</c>.</param>
    public void SetLanguage(string? languageCode)
    {
        var code = Normalize(languageCode);
        lock (_gate)
        {
            if (code == CurrentLanguage && _current.Count > 0)
            {
                PushResources();
                return;
            }

            _current = code == DefaultLanguage ? s_english.Value : LoadDictionary(code);
            CurrentLanguage = code;
        }

        PushResources();
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds the text to <paramref name="resources"/> as <c>Lang.*</c> entries and keeps them in step with the language.
    /// </summary>
    /// <param name="resources">The application's resources.</param>
    public void Attach(IResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        _resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(_resources);
        PushResources();
    }

    /// <summary>
    /// Maps a saved or system language to a supported one: any Turkish culture is <c>tr-TR</c>, anything else English.
    /// </summary>
    /// <param name="languageCode">The culture name to map.</param>
    public static string Normalize(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return DefaultLanguage;
        }

        var requested = languageCode.Trim().Split('-', '_')[0];
        var match = SupportedLanguages.FirstOrDefault(language =>
            language.Code.Split('-')[0].Equals(requested, StringComparison.OrdinalIgnoreCase));
        return match?.Code ?? DefaultLanguage;
    }

    /// <summary>
    /// Returns the language to start in when none is saved: Turkish on a Turkish system, otherwise English.
    /// </summary>
    /// <param name="culture">The system's interface culture.</param>
    public static string ResolveDefault(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return Normalize(culture.TwoLetterISOLanguageName);
    }

    /// <summary>
    /// Reads the text of one language from the embedded <c>Assets/Lang/{code}.*.json</c> files.
    /// </summary>
    /// <param name="languageCode">The culture name.</param>
    public static IReadOnlyDictionary<string, string> LoadDictionary(string languageCode)
    {
        var assembly = typeof(LocalizationService).Assembly;
        var prefix = $"Kam.Lang.{languageCode}.";
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith(prefix, StringComparison.Ordinal)
                         && name.EndsWith(".json", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                continue;
            }

            foreach (var (key, value) in Parse(stream))
            {
                values[key] = value;
            }
        }

        return values;
    }

    /// <summary>
    /// Parses a language file: one JSON object whose properties are text keys.
    /// </summary>
    /// <param name="stream">The file contents.</param>
    public static IReadOnlyDictionary<string, string> Parse(Stream stream)
    {
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                values[property.Name] = property.Value.GetString() ?? string.Empty;
            }
        }

        return values;
    }

    private void PushResources()
    {
        var resources = _resources;
        if (resources is null)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(PushResources);
            return;
        }

        IReadOnlyDictionary<string, string> current;
        lock (_gate)
        {
            current = _current;
        }

        foreach (var (key, english) in s_english.Value)
        {
            resources[ResourcePrefix + key] = current.TryGetValue(key, out var value) ? value : english;
        }
    }
}

/// <summary>
/// Short access to <see cref="LocalizationService.Instance"/> for view models.
/// </summary>
public static class Loc
{
    /// <summary>
    /// Returns the text for <paramref name="key"/> in the current language.
    /// </summary>
    /// <param name="key">The text key.</param>
    public static string Get(string key) => LocalizationService.Instance.Get(key);

    /// <summary>
    /// Formats the text for <paramref name="key"/> in the current language.
    /// </summary>
    /// <param name="key">The text key.</param>
    /// <param name="args">The placeholder values.</param>
    public static string Format(string key, params object?[] args) => LocalizationService.Instance.Format(key, args);
}
