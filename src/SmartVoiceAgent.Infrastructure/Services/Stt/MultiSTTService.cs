using Core.CrossCuttingConcerns.Logging.Serilog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Audio;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace SmartVoiceAgent.Infrastructure.Services.Stt;

/// <summary>
/// Multi-provider STT service with automatic fallback and health monitoring.
/// </summary>
public class MultiSTTService : IMultiSTTService
{
    private readonly ILogger<MultiSTTService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<STTProvider, ISpeechToTextService> _providers;
    private readonly ConcurrentDictionary<STTProvider, ProviderHealthStatus> _healthStatus;
    private readonly ConcurrentDictionary<STTProvider, STTProviderPriority> _providerPriorities;
    private readonly ConcurrentQueue<ProviderMetrics> _metrics;
    private readonly object _lock = new();
    
    private const int MaxMetricsSize = 100;
    private const int HealthCheckIntervalSeconds = 60;

    public event EventHandler<ProviderFallbackEventArgs>? OnProviderFallback;
    public event EventHandler<ProviderHealthChangedEventArgs>? OnProviderHealthChanged;

    public MultiSTTService(
        ILogger<MultiSTTService> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _providers = new ConcurrentDictionary<STTProvider, ISpeechToTextService>();
        _healthStatus = new ConcurrentDictionary<STTProvider, ProviderHealthStatus>();
        _providerPriorities = new ConcurrentDictionary<STTProvider, STTProviderPriority>();
        _metrics = new ConcurrentQueue<ProviderMetrics>();

        InitializeProviders();
        InitializeHealthStatus();
        
        _logger.LogInformation("MultiSTTService initialized with {Count} providers", _providers.Count);
    }

    private void InitializeProviders()
    {
        // Try to initialize each configured provider
        TryInitializeProvider(STTProvider.Whisper, STTProviderPriority.Primary);
        TryInitializeProvider(STTProvider.HuggingFace, STTProviderPriority.Secondary);
        TryInitializeProvider(STTProvider.Ollama, STTProviderPriority.Tertiary);
    }

    private void TryInitializeProvider(STTProvider provider, STTProviderPriority priority)
    {
        try
        {
            var service = CreateProviderService(provider);
            if (service != null)
            {
                _providers[provider] = service;
                _providerPriorities[provider] = priority;
                _logger.LogDebug("Initialized {Provider} STT provider with priority {Priority}", provider, priority);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize {Provider} STT provider", provider);
        }
    }

    private ISpeechToTextService? CreateProviderService(STTProvider provider)
    {
        try
        {
            return provider switch
            {
                STTProvider.Whisper => TryCreateWhisperService(),
                STTProvider.HuggingFace => TryCreateHuggingFaceService(),
                STTProvider.Ollama => TryCreateOllamaService(),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create {Provider} service", provider);
            return null;
        }
    }

    private ISpeechToTextService? TryCreateWhisperService()
    {
        try
        {
            var modelPath = _configuration["Whisper:ModelPath"] ?? "Models/ggml-base.bin";
            if (!File.Exists(modelPath))
            {
                _logger.LogWarning("Whisper model not found at {Path}, skipping Whisper initialization", modelPath);
                return null;
            }

            var logger = _serviceProvider.GetRequiredService<ILogger<WhisperSTTService>>();
            return new WhisperSTTService(logger, _configuration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create Whisper service");
            return null;
        }
    }

    private ISpeechToTextService? TryCreateHuggingFaceService()
    {
        try
        {
            var apiKey = _configuration["HuggingFaceConfig:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("HuggingFace API key not configured, skipping HuggingFace initialization");
                return null;
            }

            var logger = _serviceProvider.GetRequiredService<LoggerServiceBase>();
            var httpClient = new HttpClient();
            return new HuggingFaceSTTService(logger, httpClient, _configuration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create HuggingFace service");
            return null;
        }
    }

    private ISpeechToTextService? TryCreateOllamaService()
    {
        try
        {
            var endpoint = _configuration["Ollama:Endpoint"];
            if (string.IsNullOrEmpty(endpoint))
            {
                _logger.LogWarning("Ollama endpoint not configured, skipping Ollama initialization");
                return null;
            }

            var logger = _serviceProvider.GetRequiredService<ILogger<OllamaSTTService>>();
            var httpClient = new HttpClient();
            return new OllamaSTTService(httpClient, logger, _configuration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create Ollama service");
            return null;
        }
    }

    private void InitializeHealthStatus()
    {
        foreach (var provider in _providers.Keys)
        {
            _healthStatus[provider] = new ProviderHealthStatus
            {
                Provider = provider,
                IsHealthy = true,
                LastChecked = DateTime.UtcNow
            };
        }
    }

    public async Task<MultiSTTResult> ConvertToTextAsync(
        byte[] audioData,
        STTProvider? preferredProvider = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var providersTried = new List<STTProvider>();
        
        // Determine provider order
        var providersToTry = GetProvidersInPriorityOrder(preferredProvider);
        
        if (!providersToTry.Any())
        {
            return new MultiSTTResult
            {
                ErrorMessage = "No STT providers available",
                UsedProvider = STTProvider.HuggingFace, // Default value
                TotalProcessingTime = stopwatch.Elapsed
            };
        }

        foreach (var provider in providersToTry)
        {
            providersTried.Add(provider);
            
            try
            {
                _logger.LogDebug("Trying STT provider: {Provider}", provider);
                
                if (!_providers.TryGetValue(provider, out var service) || service == null)
                {
                    _logger.LogWarning("Provider {Provider} not available, skipping", provider);
                    continue;
                }

                var result = await service.ConvertToTextAsync(audioData, cancellationToken);
                
                if (result.IsSuccess)
                {
                    // Update health status
                    UpdateProviderHealth(provider, true, TimeSpan.Zero);
                    
                    stopwatch.Stop();
                    
                    var multiResult = new MultiSTTResult
                    {
                        Text = result.Text,
                        Confidence = result.Confidence,
                        ProcessingTime = result.ProcessingTime,
                        ErrorMessage = string.Empty,
                        UsedProvider = provider,
                        WasFallbackUsed = providersTried.Count > 1,
                        ProvidersTried = providersTried,
                        TotalProcessingTime = stopwatch.Elapsed
                    };

                    if (multiResult.WasFallbackUsed)
                    {
                        InvokeOnProviderFallback(new ProviderFallbackEventArgs
                        {
                            FailedProvider = providersTried[0],
                            FallbackProvider = provider,
                            Reason = $"Failed after trying {providersTried.Count - 1} provider(s)"
                        });
                    }

                    _logger.LogInformation("STT success with {Provider}: '{Text}' (Confidence: {Confidence:P0})",
                        provider, result.Text, result.Confidence);

                    return multiResult;
                }
                else
                {
                    _logger.LogWarning("Provider {Provider} returned error: {Error}", 
                        provider, result.ErrorMessage);
                    UpdateProviderHealth(provider, false, TimeSpan.Zero, result.ErrorMessage);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider {Provider} failed with exception", provider);
                UpdateProviderHealth(provider, false, TimeSpan.Zero, ex.Message);
                
                // Invoke fallback event if this was the first provider
                if (providersTried.Count == 1 && providersToTry.Count > 1)
                {
                    InvokeOnProviderFallback(new ProviderFallbackEventArgs
                    {
                        FailedProvider = provider,
                        FallbackProvider = providersToTry.Skip(1).First(),
                        Reason = ex.Message
                    });
                }
            }
        }

        // All providers failed
        stopwatch.Stop();
        
        _logger.LogError("All STT providers failed after trying {Count} provider(s)", providersTried.Count);
        
        return new MultiSTTResult
        {
            ErrorMessage = $"All STT providers failed. Tried: {string.Join(", ", providersTried)}",
            UsedProvider = providersTried.LastOrDefault(),
            WasFallbackUsed = true,
            ProvidersTried = providersTried,
            TotalProcessingTime = stopwatch.Elapsed
        };
    }

    public Task<MultiSTTResult> ConvertToTextStreamingAsync(
        byte[] audioData,
        Action<string> onInterimResult,
        CancellationToken cancellationToken = default)
    {
        // For now, delegate to non-streaming version
        // Streaming implementation would require provider-specific streaming APIs
        _logger.LogDebug("Streaming STT requested but not yet implemented, using standard conversion");
        return ConvertToTextAsync(audioData, null, cancellationToken);
    }

    public Dictionary<STTProvider, ProviderHealthStatus> GetProviderHealthStatus()
    {
        return _healthStatus.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public void SetProviderPriority(STTProvider provider, STTProviderPriority priority)
    {
        _providerPriorities[provider] = priority;
        _logger.LogInformation("Set {Provider} priority to {Priority}", provider, priority);
    }

    public async Task<TestConnectionResult[]> TestAllProvidersAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<TestConnectionResult>();
        
        foreach (var provider in _providers.Keys)
        {
            var stopwatch = Stopwatch.StartNew();
            bool isConnected = false;
            string? errorMessage = null;
            
            try
            {
                // Create a simple test - try to access the provider
                if (_providers.TryGetValue(provider, out var service) && service != null)
                {
                    // For HTTP-based services, try a simple health check
                    isConnected = await TestProviderConnectionAsync(provider, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
            }
            finally
            {
                stopwatch.Stop();
            }
            
            results.Add(new TestConnectionResult
            {
                Provider = provider,
                IsConnected = isConnected,
                ResponseTime = stopwatch.Elapsed,
                ErrorMessage = errorMessage
            });
            
            // Update health status
            UpdateProviderHealth(provider, isConnected, stopwatch.Elapsed, errorMessage);
        }
        
        return results.ToArray();
    }

    private async Task<bool> TestProviderConnectionAsync(STTProvider provider, CancellationToken cancellationToken)
    {
        try
        {
            return provider switch
            {
                STTProvider.Whisper => File.Exists(_configuration["Whisper:ModelPath"] ?? "Models/ggml-base.bin"),
                STTProvider.HuggingFace => await TestHttpEndpointAsync("https://api-inference.huggingface.co", cancellationToken),
                STTProvider.Ollama => await TestHttpEndpointAsync(_configuration["Ollama:Endpoint"] ?? "", cancellationToken),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TestHttpEndpointAsync(string url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(url)) return false;
        
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode || (int)response.StatusCode < 500;
        }
        catch
        {
            return false;
        }
    }

    private List<STTProvider> GetProvidersInPriorityOrder(STTProvider? preferredProvider)
    {
        var providers = _providers.Keys.ToList();
        
        // If preferred provider specified and available, try it first
        if (preferredProvider.HasValue && providers.Contains(preferredProvider.Value))
        {
            providers.Remove(preferredProvider.Value);
            providers.Insert(0, preferredProvider.Value);
        }
        
        // Sort by priority
        return providers
            .OrderBy(p => _providerPriorities.GetValueOrDefault(p, STTProviderPriority.Fallback))
            .ThenBy(p => !_healthStatus.GetValueOrDefault(p)?.IsHealthy ?? false) // Healthy providers first
            .ToList();
    }

    private void UpdateProviderHealth(STTProvider provider, bool success, TimeSpan responseTime, string? error = null)
    {
        if (!_healthStatus.TryGetValue(provider, out var status))
        {
            status = new ProviderHealthStatus { Provider = provider };
        }

        var oldStatus = new ProviderHealthStatus
        {
            Provider = status.Provider,
            IsHealthy = status.IsHealthy,
            SuccessCount = status.SuccessCount,
            FailureCount = status.FailureCount
        };

        lock (_lock)
        {
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
                // Mark as unhealthy if failure rate is high
                if (status.SuccessRate < 0.5 && status.SuccessCount + status.FailureCount > 5)
                {
                    status.IsHealthy = false;
                }
            }
            
            // Update average response time
            if (responseTime > TimeSpan.Zero)
            {
                var totalRequests = status.SuccessCount + status.FailureCount;
                status.AverageResponseTime = TimeSpan.FromMilliseconds(
                    (status.AverageResponseTime.TotalMilliseconds * (totalRequests - 1) + responseTime.TotalMilliseconds) / totalRequests
                );
            }
        }

        _healthStatus[provider] = status;

        // Check if health status changed
        if (oldStatus.IsHealthy != status.IsHealthy)
        {
            InvokeOnProviderHealthChanged(new ProviderHealthChangedEventArgs
            {
                Provider = provider,
                OldStatus = oldStatus,
                NewStatus = status
            });
        }
    }

    private void InvokeOnProviderFallback(ProviderFallbackEventArgs args)
    {
        try
        {
            OnProviderFallback?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in provider fallback event handler");
        }
    }

    private void InvokeOnProviderHealthChanged(ProviderHealthChangedEventArgs args)
    {
        try
        {
            OnProviderHealthChanged?.Invoke(this, args);
            _logger.LogInformation("Provider {Provider} health changed: Healthy={IsHealthy}, SuccessRate={SuccessRate:P0}",
                args.Provider, args.NewStatus.IsHealthy, args.NewStatus.SuccessRate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in provider health changed event handler");
        }
    }

    public void Dispose()
    {
        foreach (var provider in _providers.Values)
        {
            try
            {
                provider?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing STT provider");
            }
        }
        
        _providers.Clear();
        _logger.LogDebug("MultiSTTService disposed");
    }

    private class ProviderMetrics
    {
        public STTProvider Provider { get; set; }
        public bool Success { get; set; }
        public TimeSpan ResponseTime { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
