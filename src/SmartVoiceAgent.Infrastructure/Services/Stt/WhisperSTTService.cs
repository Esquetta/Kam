using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Audio;
using SmartVoiceAgent.Infrastructure.Services.Voice;
using System.Diagnostics;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Transcribes speech on this computer with Whisper. The spoken language and model come from Settings
/// (<c>Voice:Language</c>, <c>Voice:LocalModel</c>) on each call; models load on first use and stay loaded.
/// </summary>
public class WhisperSTTService : ISpeechToTextService
{
    private readonly ILogger<WhisperSTTService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ISpeechModelStore _modelStore;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, WhisperFactory> _factories = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Path, string Language, string Prompt), WhisperProcessor> _processors = [];
    private bool _disposed;

    /// <summary>
    /// Creates the service. Models load on the first transcription, not at startup.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="configuration">The configuration Settings write to.</param>
    /// <param name="modelStore">Where downloaded models are kept.</param>
    public WhisperSTTService(
        ILogger<WhisperSTTService> logger,
        IConfiguration configuration,
        ISpeechModelStore? modelStore = null)
    {
        _logger = logger;
        _configuration = configuration;
        _modelStore = modelStore ?? new WhisperModelStore(Microsoft.Extensions.Logging.Abstractions.NullLogger<WhisperModelStore>.Instance);
    }

    /// <summary>
    /// Gets whether a Whisper model has been loaded.
    /// </summary>
    public bool IsModelLoaded
    {
        get
        {
            lock (_factories)
            {
                return _factories.Count > 0;
            }
        }
    }

    /// <summary>
    /// Returns the model file Settings select, or null when it isn't on this computer.
    /// </summary>
    /// <param name="model">A model name to use instead of the one in Settings.</param>
    public string? ResolveModelPath(string? model = null)
    {
        var settings = VoiceSettings.Read(_configuration);
        if (model is null && settings.LocalModelPath is not null)
        {
            return File.Exists(settings.LocalModelPath) ? settings.LocalModelPath : null;
        }

        var name = model ?? settings.LocalModel;
        return _modelStore.IsDownloaded(name) ? _modelStore.GetModelPath(name) : null;
    }

    /// <inheritdoc />
    public Task<SpeechResult> ConvertToTextAsync(byte[] audioData, CancellationToken cancellationToken = default)
    {
        return TranscribeAsync(audioData, model: null, language: null, prompt: null, cancellationToken);
    }

    /// <summary>
    /// Transcribes raw 16 kHz mono 16-bit PCM or WAV audio.
    /// </summary>
    /// <param name="audioData">The audio.</param>
    /// <param name="model">The model to use, or null for the one in Settings.</param>
    /// <param name="language">A two-letter language or <c>auto</c>, or null for the one in Settings.</param>
    /// <param name="prompt">Text that steers recognition toward expected words, such as the wake phrase.</param>
    /// <param name="cancellationToken">Stops the transcription.</param>
    public async Task<SpeechResult> TranscribeAsync(
        byte[] audioData,
        string? model,
        string? language,
        string? prompt,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            ArgumentNullException.ThrowIfNull(audioData);
            var settings = VoiceSettings.Read(_configuration);
            var path = ResolveModelPath(model)
                ?? throw new FileNotFoundException(
                    $"The speech model '{model ?? settings.LocalModel}' isn't downloaded.",
                    model is null && settings.LocalModelPath is not null
                        ? settings.LocalModelPath
                        : _modelStore.GetModelPath(model ?? settings.LocalModel));
            var samples = WaveAudio.ToSamples(audioData);
            if (samples.Length < WaveAudio.SampleRate / 10)
            {
                return new SpeechResult { ProcessingTime = stopwatch.Elapsed, ErrorMessage = "The recording is too short." };
            }

            var segments = new List<SegmentData>();
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var processor = await Task.Run(
                        () => GetProcessor(path, language ?? settings.Language, prompt ?? string.Empty),
                        cancellationToken)
                    .ConfigureAwait(false);
                await foreach (var segment in processor.ProcessAsync(samples, cancellationToken).ConfigureAwait(false))
                {
                    if (segment.NoSpeechProbability < 0.8f)
                    {
                        segments.Add(segment);
                    }
                }
            }
            finally
            {
                _gate.Release();
            }

            var text = TranscriptCleaner.Clean(string.Join(" ", segments.Select(segment => segment.Text.Trim())));
            var confidence = segments.Count > 0 ? segments.Average(segment => segment.Probability) : 0f;
            _logger.LogInformation(
                "Local transcription took {Elapsed} ms: {Characters} characters",
                stopwatch.ElapsedMilliseconds,
                text.Length);

            return new SpeechResult
            {
                Text = text,
                Confidence = text.Length == 0 ? 0f : Math.Max(confidence, 0.31f),
                ProcessingTime = stopwatch.Elapsed,
                ErrorMessage = text.Length == 0 ? TranscriptCleaner.NoSpeechMessage : string.Empty
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Local transcription failed");
            return new SpeechResult
            {
                ProcessingTime = stopwatch.Elapsed,
                ErrorMessage = ex.Message
            };
        }
    }

    private WhisperProcessor GetProcessor(string path, string language, string prompt)
    {
        var key = (path, language, prompt);
        if (_processors.TryGetValue(key, out var processor))
        {
            return processor;
        }

        WhisperFactory factory;
        lock (_factories)
        {
            if (!_factories.TryGetValue(path, out factory!))
            {
                factory = WhisperFactory.FromPath(path);
                _factories[path] = factory;
                _logger.LogInformation(
                    "Local speech model loaded from {ModelPath} on the {WhisperRuntime} runtime",
                    path,
                    RuntimeOptions.LoadedLibrary?.ToString() ?? "unknown");
            }
        }

        var builder = factory.CreateBuilder()
            .WithLanguage(language)
            .WithNoSpeechThreshold(0.6f)
            .WithProbabilities()
            .WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 1, 8));
        if (prompt.Length > 0)
        {
            builder = builder.WithPrompt(prompt);
        }

        processor = builder.Build();
        _processors[key] = processor;
        return processor;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gate.Wait();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var processor in _processors.Values)
            {
                processor.Dispose();
            }

            _processors.Clear();
            lock (_factories)
            {
                foreach (var factory in _factories.Values)
                {
                    factory.Dispose();
                }

                _factories.Clear();
            }
        }
        finally
        {
            _gate.Release();
        }

        GC.SuppressFinalize(this);
    }
}
