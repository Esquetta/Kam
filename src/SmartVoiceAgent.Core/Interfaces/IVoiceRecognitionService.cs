﻿namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Service interface for voice recognition operations.
/// </summary>
public interface IVoiceRecognitionService
{
    /// <summary>
    /// Starts listening for voice input asynchronously.
    /// </summary>
    /// <returns>The recognized text.</returns>
    Task<string> ListenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the voice recognition service.
    /// </summary>
    Task StopAsync();
}
