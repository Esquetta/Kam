using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Service for voice recognition operations.
/// </summary>
public class VoiceRecognitionService : IVoiceRecognitionService
{
    public Task<string> ListenAsync()
    {
        // TODO: Implement platform-specific voice recognition logic.
        return Task.FromResult("Sample recognized voice input.");
    }

    public Task<string> ListenAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Implement platform-specific voice recognition logic.
        return Task.FromResult("Listening...");
    }

    public Task StopAsync()
    {
        // TODO: Implement platform-specific voice recognition logic.
        return Task.FromResult("Stopping...");
    }
}
