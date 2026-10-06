using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Audio;
using System.Buffers.Binary;
using System.Diagnostics;
using Whisper.net;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Local Whisper.NET implementation for Speech-to-Text service
/// Runs completely offline without API calls
/// </summary>
public class WhisperSTTService : ISpeechToTextService
{
    private readonly ILogger<WhisperSTTService> _logger;
    private readonly string _modelPath;
    private readonly object _loadGate = new();
    private WhisperFactory? _whisperFactory;
    private WhisperProcessor? _processor;
    private bool _disposed = false;

    /// <summary>
    /// Creates the service. The model loads on the first transcription, not at startup,
    /// so the app opens without reading a model of several hundred megabytes.
    /// </summary>
    public WhisperSTTService(ILogger<WhisperSTTService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _modelPath = configuration["Whisper:ModelPath"] ?? "Models/ggml-base.bin";
    }

    /// <summary>
    /// Gets whether the Whisper model has been loaded.
    /// </summary>
    public bool IsModelLoaded => _processor is not null;

    private WhisperProcessor GetProcessor()
    {
        lock (_loadGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_processor is not null)
            {
                return _processor;
            }

            _whisperFactory = WhisperFactory.FromPath(_modelPath);
            _processor = _whisperFactory.CreateBuilder()
                .WithLanguage("auto")
                .WithPrintProgress()
                .WithNoSpeechThreshold(0.6f)
                .WithProbabilities()
                .Build();

            _logger.LogInformation("Local Whisper model loaded from {ModelPath}", _modelPath);
            return _processor;
        }
    }

    public async Task<SpeechResult> ConvertToTextAsync(byte[] audioData, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation($"Processing audio data: {audioData.Length} bytes");

            // Audio data'yı WAV formatına çevir (gerekirse)
            var processedAudio = await PreprocessAudioAsync(audioData);

            // Whisper ile işle
            var segments = new List<SegmentData>();
            var processor = await Task.Run(GetProcessor, cancellationToken);
            await foreach (var segment in processor.ProcessAsync(processedAudio, cancellationToken))
            {
                segments.Add(segment);
                _logger.LogDebug($"Segment: {segment.Text} (Confidence: {segment.Probability:F2})");
            }

            stopwatch.Stop();

            // Sonuçları birleştir
            var fullText = string.Join(" ", segments.Select(s => s.Text.Trim())).Trim();
            var avgConfidence = segments.Any() ? segments.Average(s => s.Probability) : 0f;

            _logger.LogInformation($"STT completed in {stopwatch.ElapsedMilliseconds}ms: '{fullText}'");

            return new SpeechResult
            {
                Text = fullText,
                Confidence = avgConfidence,
                ProcessingTime = stopwatch.Elapsed,
                ErrorMessage = string.Empty
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error in local Whisper STT conversion");

            return new SpeechResult
            {
                Text = string.Empty,
                Confidence = 0f,
                ProcessingTime = stopwatch.Elapsed,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<float[]> PreprocessAudioAsync(byte[] audioData)
    {
        // Audio preprocessing - WAV formatından float array'e çevir
        // Bu basit bir implementasyon, gerçek projede NAudio kullanabilirsiniz

        if (audioData.Length < 44) // WAV header minimum size
        {
            throw new ArgumentException("Invalid audio data - too short");
        }

        // WAV header'ı atla (basit implementasyon)
        var audioBytes = audioData.AsSpan(44);

        // 16-bit PCM'den float'a çevir
        var samples = new float[audioBytes.Length / 2];
        for (int i = 0; i < samples.Length; i++)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(audioBytes.Slice(i * 2, 2));
            samples[i] = sample / 32768f; // Normalize to -1.0 to 1.0
        }

        return samples;
    }

    public void Dispose()
    {
        lock (_loadGate)
        {
            if (!_disposed)
            {
                _processor?.Dispose();
                _whisperFactory?.Dispose();
                _disposed = true;
            }
        }
    }
}
