using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Services.Voice;

namespace SmartVoiceAgent.Infrastructure.Services.Speech;

/// <summary>
/// Reads text aloud with this platform's speech engine: Windows voices (including the newer OneCore voices
/// such as Turkish "Tolga"), <c>say</c> on macOS, or <c>spd-say</c>/<c>espeak-ng</c> on Linux. Voice, rate and
/// language come from <c>Voice:*</c> on each call.
/// </summary>
public sealed class TextToSpeechService : ITextToSpeechService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TextToSpeechService> _logger;
    private readonly ISpeechSynthesizer? _synthesizer;
    private readonly object _gate = new();
    private CancellationTokenSource? _current;

    /// <summary>
    /// Creates the service with the platform's synthesizer.
    /// </summary>
    /// <param name="configuration">The configuration Settings write to.</param>
    /// <param name="logger">The logger.</param>
    public TextToSpeechService(IConfiguration configuration, ILogger<TextToSpeechService> logger)
        : this(configuration, logger, CreatePlatformSynthesizer())
    {
    }

    /// <summary>
    /// Creates the service with <paramref name="synthesizer"/>.
    /// </summary>
    /// <param name="configuration">The configuration Settings write to.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="synthesizer">The synthesizer, or null when the platform has none.</param>
    public TextToSpeechService(IConfiguration configuration, ILogger<TextToSpeechService> logger, ISpeechSynthesizer? synthesizer)
    {
        _configuration = configuration;
        _logger = logger;
        _synthesizer = synthesizer;
    }

    /// <inheritdoc />
    public bool IsAvailable => _synthesizer?.IsAvailable == true;

    /// <inheritdoc />
    public bool IsSpeaking
    {
        get
        {
            lock (_gate)
            {
                return _current is not null;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<SpeechVoiceInfo> GetVoices()
    {
        try
        {
            return _synthesizer?.GetVoices() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list speech voices");
            return [];
        }
    }

    /// <summary>
    /// Picks the voice for <paramref name="language"/>: the chosen voice when it is installed, otherwise the first
    /// voice in that language, otherwise null for the system default.
    /// </summary>
    /// <param name="voices">The installed voices.</param>
    /// <param name="chosenVoiceId">The voice chosen in Settings, or null.</param>
    /// <param name="language">The two-letter language of the text.</param>
    public static SpeechVoiceInfo? PickVoice(IReadOnlyList<SpeechVoiceInfo> voices, string? chosenVoiceId, string language)
    {
        var chosen = voices.FirstOrDefault(voice => voice.Id == chosenVoiceId);
        if (chosen is not null && (chosen.Language.Length == 0 || chosen.Language == language))
        {
            return chosen;
        }

        return voices.FirstOrDefault(voice => voice.Language == language) ?? chosen;
    }

    /// <inheritdoc />
    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_synthesizer is not { IsAvailable: true } || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var settings = VoiceSettings.Read(_configuration);
        var language = settings.Language == VoiceSettings.AutoLanguage
            ? SpeechTextFormatter.GuessLanguage(text)
            : settings.Language;
        var voice = PickVoice(GetVoices(), settings.SpeechVoice, language);

        CancellationTokenSource linked;
        lock (_gate)
        {
            _current?.Cancel();
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _current = linked;
        }

        try
        {
            foreach (var sentence in SpeechTextFormatter.SplitSentences(text))
            {
                linked.Token.ThrowIfCancellationRequested();
                await _synthesizer.SpeakAsync(sentence, voice?.Id, language, settings.SpeechRate, settings.OutputDeviceId, linked.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reading aloud failed");
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_current, linked))
                {
                    _current = null;
                }
            }

            linked.Dispose();
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_gate)
        {
            _current?.Cancel();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        (_synthesizer as IDisposable)?.Dispose();
    }

    private static ISpeechSynthesizer? CreatePlatformSynthesizer()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsSpeechSynthesizer();
        }

        return CommandLineSpeechSynthesizer.TryCreate();
    }
}

/// <summary>
/// A platform speech engine.
/// </summary>
public interface ISpeechSynthesizer
{
    /// <summary>
    /// Gets whether the engine works on this computer.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Returns the installed voices.
    /// </summary>
    IReadOnlyList<SpeechVoiceInfo> GetVoices();

    /// <summary>
    /// Reads one sentence aloud and completes when it is done or cancelled.
    /// </summary>
    /// <param name="text">The sentence.</param>
    /// <param name="voiceId">The voice, or null for the default one.</param>
    /// <param name="language">The two-letter language of the sentence.</param>
    /// <param name="rate">The speaking rate from -5 to 5.</param>
    /// <param name="outputDeviceId">The speaker to play on, or null for the default one.</param>
    /// <param name="cancellationToken">Stops reading.</param>
    Task SpeakAsync(string text, string? voiceId, string language, int rate, string? outputDeviceId, CancellationToken cancellationToken);
}
