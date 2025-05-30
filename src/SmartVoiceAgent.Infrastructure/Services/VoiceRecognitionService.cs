using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Service for voice recognition operations.
/// </summary>
public class VoiceRecognitionService : IVoiceRecognitionService
{
    public Task<string> ListenAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Gerçek ses tanıma altyapısı buraya entegre edilecek.
        return Task.FromResult("dummy recognized command");
    }

    public Task StopAsync()
    {
        // TODO: Mikrofon dinleme işlemini durdur.
        return Task.CompletedTask;
    }
}
