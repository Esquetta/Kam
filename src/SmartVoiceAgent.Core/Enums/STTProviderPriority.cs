namespace SmartVoiceAgent.Core.Enums;

/// <summary>
/// Priority levels for STT provider fallback chain.
/// </summary>
public enum STTProviderPriority
{
    /// <summary>
    /// Primary provider - used first for all requests.
    /// </summary>
    Primary = 0,
    
    /// <summary>
    /// Secondary provider - used when primary fails.
    /// </summary>
    Secondary = 1,
    
    /// <summary>
    /// Tertiary provider - used as last resort.
    /// </summary>
    Tertiary = 2,
    
    /// <summary>
    /// Fallback provider - only used when all others fail.
    /// </summary>
    Fallback = 3
}

/// <summary>
/// Configuration modes for automatic provider selection.
/// </summary>
public enum STTAutoSelectMode
{
    /// <summary>
    /// Always use the primary configured provider.
    /// </summary>
    Fixed,
    
    /// <summary>
    /// Use local provider (Whisper) when offline, cloud when online.
    /// </summary>
    OnlineOffline,
    
    /// <summary>
    /// Select provider based on audio language.
    /// </summary>
    LanguageBased,
    
    /// <summary>
    /// Select provider based on audio quality/characteristics.
    /// </summary>
    QualityBased,
    
    /// <summary>
    /// Automatic selection with fallback chain.
    /// </summary>
    SmartFallback
}
