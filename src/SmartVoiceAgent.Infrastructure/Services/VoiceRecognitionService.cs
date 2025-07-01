using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Service for voice recognition operations.
/// </summary>
public class VoiceRecognitionService : IVoiceRecognitionService
{
    public event EventHandler<byte[]> OnVoiceCaptured;

    public Task<string> ListenAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Gerçek ses tanıma altyapısı buraya entegre edilecek.
        return Task.FromResult("dummy recognized command");
    }

    public void StartRecording()
    {
        throw new NotImplementedException();
    }

    public Task StopAsync()
    {
        // TODO: Mikrofon dinleme işlemini durdur.
        return Task.CompletedTask;
    }

    public void StopRecording()
    {
        throw new NotImplementedException();
    }
}
