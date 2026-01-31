using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Models.Audio;

namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Multi-provider Speech-to-Text service with automatic fallback support.
/// </summary>
public interface IMultiSTTService : IDisposable
{
    /// <summary>
    /// Converts audio to text using the best available provider with automatic fallback.
    /// </summary>
    /// <param name="audioData">Raw audio data bytes</param>
    /// <param name="preferredProvider">Optional preferred provider (null for auto-selection)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Speech result with provider information</returns>
    Task<MultiSTTResult> ConvertToTextAsync(
        byte[] audioData, 
        STTProvider? preferredProvider = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts audio to text with streaming support (interim results).
    /// </summary>
    /// <param name="audioData">Raw audio data bytes</param>
    /// <param name="onInterimResult">Callback for interim results during processing</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Final speech result</returns>
    Task<MultiSTTResult> ConvertToTextStreamingAsync(
        byte[] audioData,
        Action<string> onInterimResult,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current provider health status.
    /// </summary>
    Dictionary<STTProvider, ProviderHealthStatus> GetProviderHealthStatus();

    /// <summary>
    /// Sets the priority for a specific provider.
    /// </summary>
    void SetProviderPriority(STTProvider provider, STTProviderPriority priority);

    /// <summary>
    /// Tests connectivity to all configured providers.
    /// </summary>
    Task<TestConnectionResult[]> TestAllProvidersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when provider fallback occurs.
    /// </summary>
    event EventHandler<ProviderFallbackEventArgs>? OnProviderFallback;

    /// <summary>
    /// Event raised when a provider's health status changes.
    /// </summary>
    event EventHandler<ProviderHealthChangedEventArgs>? OnProviderHealthChanged;
}

/// <summary>
/// Extended speech result with provider information.
/// </summary>
public class MultiSTTResult : SpeechResult
{
    /// <summary>
    /// The STT provider that produced this result.
    /// </summary>
    public STTProvider UsedProvider { get; set; }
    
    /// <summary>
    /// Whether fallback was used to get this result.
    /// </summary>
    public bool WasFallbackUsed { get; set; }
    
    /// <summary>
    /// List of providers that were tried before success.
    /// </summary>
    public List<STTProvider> ProvidersTried { get; set; } = new();
    
    /// <summary>
    /// Total time including fallback attempts.
    /// </summary>
    public TimeSpan TotalProcessingTime { get; set; }
}

/// <summary>
/// Health status for an STT provider.
/// </summary>
public class ProviderHealthStatus
{
    public STTProvider Provider { get; set; }
    public bool IsHealthy { get; set; }
    public DateTime LastChecked { get; set; }
    public TimeSpan AverageResponseTime { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public double SuccessRate => SuccessCount + FailureCount > 0 
        ? (double)SuccessCount / (SuccessCount + FailureCount) 
        : 0;
    public string? LastError { get; set; }
}

/// <summary>
/// Result of a connection test.
/// </summary>
public class TestConnectionResult
{
    public STTProvider Provider { get; set; }
    public bool IsConnected { get; set; }
    public TimeSpan ResponseTime { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Event args for provider fallback events.
/// </summary>
public class ProviderFallbackEventArgs : System.EventArgs
{
    public STTProvider FailedProvider { get; set; }
    public STTProvider FallbackProvider { get; set; }
    public string? Reason { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Event args for provider health change events.
/// </summary>
public class ProviderHealthChangedEventArgs : System.EventArgs
{
    public STTProvider Provider { get; set; }
    public ProviderHealthStatus OldStatus { get; set; }
    public ProviderHealthStatus NewStatus { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
