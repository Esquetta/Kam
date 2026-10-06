using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace SmartVoiceAgent.Infrastructure.Services.Voice;

/// <summary>
/// The voice options Settings write under <c>Voice:*</c>. Services read them on each use, so a change in
/// Settings applies to the next recording without a restart.
/// </summary>
public sealed record VoiceSettings
{
    /// <summary>
    /// The configuration section that holds the voice options.
    /// </summary>
    public const string SectionName = "Voice";

    /// <summary>
    /// The speech engine that runs Whisper on this computer.
    /// </summary>
    public const string LocalEngine = "Local";

    /// <summary>
    /// The speech engine that calls an OpenAI-compatible <c>/audio/transcriptions</c> endpoint.
    /// </summary>
    public const string ApiEngine = "OpenAI";

    /// <summary>
    /// The value of <see cref="Language"/> that lets Whisper detect the spoken language.
    /// </summary>
    public const string AutoLanguage = "auto";

    /// <summary>
    /// The transcription endpoint used when none is set.
    /// </summary>
    public const string DefaultApiEndpoint = "https://api.openai.com/v1";

    /// <summary>
    /// The transcription model used when none is set.
    /// </summary>
    public const string DefaultApiModel = "whisper-1";

    /// <summary>
    /// Gets the spoken language as a two-letter code such as <c>tr</c>, or <c>auto</c>.
    /// </summary>
    public string Language { get; init; } = AutoLanguage;

    /// <summary>
    /// Gets the speech engine: <see cref="LocalEngine"/> or <see cref="ApiEngine"/>.
    /// </summary>
    public string SpeechEngine { get; init; } = LocalEngine;

    /// <summary>
    /// Gets the local Whisper model name, such as <c>base</c>.
    /// </summary>
    public string LocalModel { get; init; } = SpeechModelCatalog.DefaultModel;

    /// <summary>
    /// Gets a Whisper model file to use instead of the downloaded one (the older <c>Whisper:ModelPath</c> option).
    /// </summary>
    public string? LocalModelPath { get; init; }

    /// <summary>
    /// Gets the base URL of the OpenAI-compatible transcription API.
    /// </summary>
    public string ApiEndpoint { get; init; } = DefaultApiEndpoint;

    /// <summary>
    /// Gets the transcription model of the OpenAI-compatible API.
    /// </summary>
    public string ApiModel { get; init; } = DefaultApiModel;

    /// <summary>
    /// Gets the API key of the transcription API.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Gets whether an endpoint or key was set for the transcription API.
    /// </summary>
    public bool HasApi { get; init; }

    /// <summary>
    /// Gets the microphone to record from (a Windows audio endpoint id); empty uses the default microphone.
    /// </summary>
    public string? InputDeviceId { get; init; }

    /// <summary>
    /// Gets the speaker replies play on (a Windows audio endpoint id); empty uses the default speaker.
    /// </summary>
    public string? OutputDeviceId { get; init; }

    /// <summary>
    /// Gets the wake phrase, such as <c>Hey Kam</c>.
    /// </summary>
    public string WakeWord { get; init; } = "Hey Kam";

    /// <summary>
    /// Gets the voice replies are read in; empty picks one for <see cref="Language"/>.
    /// </summary>
    public string? SpeechVoice { get; init; }

    /// <summary>
    /// Gets the speaking rate from -5 (slow) to 5 (fast).
    /// </summary>
    public int SpeechRate { get; init; }

    /// <summary>
    /// Gets whether the API engine is selected.
    /// </summary>
    public bool UsesApi => string.Equals(SpeechEngine, ApiEngine, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the voice options from configuration.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    public static VoiceSettings Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);
        var endpoint = Clean(section["SpeechApi:Endpoint"]);
        var apiKey = Clean(section["SpeechApi:ApiKey"]);
        return new VoiceSettings
        {
            Language = NormalizeLanguage(section["Language"]),
            SpeechEngine = string.Equals(Clean(section["SpeechEngine"]), ApiEngine, StringComparison.OrdinalIgnoreCase)
                ? ApiEngine
                : LocalEngine,
            LocalModel = SpeechModelCatalog.Normalize(section["LocalModel"]),
            LocalModelPath = Clean(configuration["Whisper:ModelPath"]),
            ApiEndpoint = endpoint ?? DefaultApiEndpoint,
            ApiModel = Clean(section["SpeechApi:Model"]) ?? DefaultApiModel,
            ApiKey = apiKey,
            HasApi = endpoint is not null || apiKey is not null,
            InputDeviceId = Clean(section["InputDeviceId"]),
            OutputDeviceId = Clean(section["OutputDeviceId"]),
            WakeWord = Clean(section["WakeWord"]) ?? "Hey Kam",
            SpeechVoice = Clean(section["SpeechVoice"]),
            SpeechRate = int.TryParse(section["SpeechRate"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rate)
                ? Math.Clamp(rate, -5, 5)
                : 0
        };
    }

    /// <summary>
    /// Turns a language setting such as <c>tr-TR</c>, <c>TR</c> or empty into a two-letter code or <c>auto</c>.
    /// </summary>
    /// <param name="language">The language setting.</param>
    public static string NormalizeLanguage(string? language)
    {
        var value = Clean(language);
        if (value is null || value.Equals(AutoLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return AutoLanguage;
        }

        var parts = value.ToLowerInvariant().Split('-', '_');
        var code = parts[0];
        var valid = code.Length is 2 or 3
            && code.All(char.IsAsciiLetterLower)
            && parts.Skip(1).All(part => part.Length is >= 2 and <= 8 && part.All(char.IsAsciiLetterOrDigit));
        return valid ? code : AutoLanguage;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
