using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Services.Voice;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace SmartVoiceAgent.Infrastructure.Services.Stt;

/// <summary>
/// Transcribes with the speech engine chosen in Settings and falls back to the other configured engines.
/// Providers are picked on each call from <c>Voice:*</c>, so a Settings change applies to the next recording.
/// </summary>
public class MultiSTTService : IMultiSTTService
{
    private readonly ILogger<MultiSTTService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<STTProvider, ProviderHealthStatus> _healthStatus = new();
    private readonly ConcurrentDictionary<STTProvider, STTProviderPriority> _priorityOverrides = new();
    private readonly object _healthGate = new();

    /// <inheritdoc />
    public event EventHandler<ProviderFallbackEventArgs>? OnProviderFallback;

    /// <inheritdoc />
    public event EventHandler<ProviderHealthChangedEventArgs>? OnProviderHealthChanged;

    /// <summary>
    /// Creates the service. Nothing is loaded until the first transcription.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="configuration">The configuration Settings write to.</param>
    /// <param name="serviceProvider">Resolves the provider services.</param>
    public MultiSTTService(
        ILogger<MultiSTTService> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Returns the providers to try, best first, for the current Settings.
    /// </summary>
    public IReadOnlyList<STTProvider> GetProviderOrder()
    {
        var settings = VoiceSettings.Read(_configuration);
        var whisperReady = _serviceProvider.GetService<WhisperSTTService>()?.ResolveModelPath() is not null;
        var huggingFaceReady = !string.IsNullOrWhiteSpace(_configuration["HuggingFaceConfig:ApiKey"]);

        var order = new List<STTProvider>();
        if (settings.UsesApi)
        {
            if (settings.HasApi)
            {
                order.Add(STTProvider.OpenAI);
            }

            if (whisperReady)
            {
                order.Add(STTProvider.Whisper);
            }
        }
        else
        {
            if (whisperReady)
            {
                order.Add(STTProvider.Whisper);
            }

            if (settings.HasApi)
            {
                order.Add(STTProvider.OpenAI);
            }
        }

        if (huggingFaceReady)
        {
            order.Add(STTProvider.HuggingFace);
        }

        return order
            .Select((provider, index) => (provider, index))
            .OrderBy(item => _priorityOverrides.TryGetValue(item.provider, out var priority) ? (int)priority : int.MaxValue)
            .ThenBy(item => item.index)
            .Select(item => item.provider)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<MultiSTTResult> ConvertToTextAsync(
        byte[] audioData,
        STTProvider? preferredProvider = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var order = GetProviderOrder().ToList();
        if (preferredProvider is { } preferred && order.Remove(preferred))
        {
            order.Insert(0, preferred);
        }

        if (order.Count == 0)
        {
            return new MultiSTTResult
            {
                ErrorMessage = "No speech engine is ready. Download the local model or set the transcription API in Settings.",
                TotalProcessingTime = stopwatch.Elapsed
            };
        }

        var tried = new List<STTProvider>();
        var lastError = string.Empty;
        foreach (var provider in order)
        {
            tried.Add(provider);
            var service = Resolve(provider);
            if (service is null)
            {
                continue;
            }

            var attempt = Stopwatch.StartNew();
            var result = await service.ConvertToTextAsync(audioData, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(result.Text))
            {
                UpdateHealth(provider, true, attempt.Elapsed);
                if (tried.Count > 1)
                {
                    RaiseFallback(new ProviderFallbackEventArgs
                    {
                        FailedProvider = tried[0],
                        FallbackProvider = provider,
                        Reason = lastError
                    });
                }

                return new MultiSTTResult
                {
                    Text = result.Text,
                    Confidence = result.Confidence,
                    ProcessingTime = result.ProcessingTime,
                    UsedProvider = provider,
                    WasFallbackUsed = tried.Count > 1,
                    ProvidersTried = tried,
                    TotalProcessingTime = stopwatch.Elapsed
                };
            }

            lastError = result.ErrorMessage;
            if (lastError == TranscriptCleaner.NoSpeechMessage)
            {
                // The engine worked and heard nothing; another engine won't hear more.
                UpdateHealth(provider, true, attempt.Elapsed);
                break;
            }

            _logger.LogWarning("Speech engine {Provider} failed: {Error}", provider, lastError);
            UpdateHealth(provider, false, attempt.Elapsed, lastError);
        }

        return new MultiSTTResult
        {
            ErrorMessage = lastError,
            UsedProvider = tried.LastOrDefault(),
            WasFallbackUsed = tried.Count > 1,
            ProvidersTried = tried,
            TotalProcessingTime = stopwatch.Elapsed
        };
    }

    /// <inheritdoc />
    public Task<MultiSTTResult> ConvertToTextStreamingAsync(
        byte[] audioData,
        Action<string> onInterimResult,
        CancellationToken cancellationToken = default)
    {
        return ConvertToTextAsync(audioData, null, cancellationToken);
    }

    /// <inheritdoc />
    public Dictionary<STTProvider, ProviderHealthStatus> GetProviderHealthStatus()
    {
        return _healthStatus.ToDictionary(item => item.Key, item => item.Value);
    }

    /// <inheritdoc />
    public void SetProviderPriority(STTProvider provider, STTProviderPriority priority)
    {
        _priorityOverrides[provider] = priority;
    }

    /// <inheritdoc />
    public async Task<TestConnectionResult[]> TestAllProvidersAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<TestConnectionResult>();
        var settings = VoiceSettings.Read(_configuration);
        foreach (var provider in GetProviderOrder())
        {
            var stopwatch = Stopwatch.StartNew();
            string? error = null;
            var connected = false;
            try
            {
                connected = provider switch
                {
                    STTProvider.Whisper => true,
                    STTProvider.OpenAI => await PingAsync(settings, cancellationToken).ConfigureAwait(false),
                    STTProvider.HuggingFace => true,
                    _ => false
                };
            }
            catch (HttpRequestException ex)
            {
                error = ex.Message;
            }

            results.Add(new TestConnectionResult
            {
                Provider = provider,
                IsConnected = connected,
                ResponseTime = stopwatch.Elapsed,
                ErrorMessage = error
            });
        }

        return [.. results];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private ISpeechToTextService? Resolve(STTProvider provider)
    {
        return provider switch
        {
            STTProvider.Whisper => _serviceProvider.GetService<WhisperSTTService>(),
            STTProvider.OpenAI => _serviceProvider.GetService<OpenAiTranscriptionService>(),
            STTProvider.HuggingFace => _serviceProvider.GetService<HuggingFaceSTTService>(),
            _ => null
        };
    }

    private async Task<bool> PingAsync(VoiceSettings settings, CancellationToken cancellationToken)
    {
        var transcriptions = OpenAiTranscriptionService.BuildRequestUri(settings.ApiEndpoint);
        if (transcriptions is null)
        {
            return false;
        }

        var models = new Uri(transcriptions, "../models");
        using var request = new HttpRequestMessage(HttpMethod.Get, models);
        if (settings.ApiKey is not null)
        {
            request.Headers.Authorization = new global::System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }

        var client = _serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(OpenAiTranscriptionService.HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    private void UpdateHealth(STTProvider provider, bool success, TimeSpan elapsed, string? error = null)
    {
        ProviderHealthStatus before;
        ProviderHealthStatus after;
        lock (_healthGate)
        {
            var status = _healthStatus.GetOrAdd(provider, key => new ProviderHealthStatus { Provider = key, IsHealthy = true });
            before = new ProviderHealthStatus
            {
                Provider = provider,
                IsHealthy = status.IsHealthy,
                SuccessCount = status.SuccessCount,
                FailureCount = status.FailureCount
            };

            status.LastChecked = DateTime.UtcNow;
            if (success)
            {
                status.SuccessCount++;
                status.IsHealthy = true;
                status.LastError = null;
            }
            else
            {
                status.FailureCount++;
                status.LastError = error;
                status.IsHealthy = status.SuccessRate >= 0.5 || status.SuccessCount + status.FailureCount <= 3;
            }

            var total = status.SuccessCount + status.FailureCount;
            status.AverageResponseTime = TimeSpan.FromMilliseconds(
                (status.AverageResponseTime.TotalMilliseconds * (total - 1) + elapsed.TotalMilliseconds) / total);
            after = status;
        }

        if (before.IsHealthy != after.IsHealthy)
        {
            OnProviderHealthChanged?.Invoke(this, new ProviderHealthChangedEventArgs
            {
                Provider = provider,
                OldStatus = before,
                NewStatus = after
            });
        }
    }

    private void RaiseFallback(ProviderFallbackEventArgs args)
    {
        try
        {
            OnProviderFallback?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Speech engine fallback handler failed");
        }
    }
}
