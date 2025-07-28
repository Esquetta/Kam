using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Service for voice recognition operations.
/// </summary>
public class VoiceRecognitionService : IVoiceRecognitionService
{
    private bool disposedValue;

    public bool IsRecording => throw new NotImplementedException();

    public bool IsListening => throw new NotImplementedException();

    public event EventHandler<byte[]> OnVoiceCaptured;
    public event EventHandler<Exception> OnError;
    public event EventHandler OnRecordingStarted;
    public event EventHandler OnRecordingStopped;
    public event EventHandler OnListeningStarted;
    public event EventHandler OnListeningStopped;

    public void ClearBuffer()
    {
        throw new NotImplementedException();
    }

    public long GetCurrentBufferSize()
    {
        throw new NotImplementedException();
    }

    public Task<string> ListenAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Gerçek ses tanıma altyapısı buraya entegre edilecek.
        return Task.FromResult("dummy recognized command");
    }

    public Task<byte[]> RecordForDurationAsync(TimeSpan duration)
    {
        throw new NotImplementedException();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~VoiceRecognitionService()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public void StartListening()
    {
        throw new NotImplementedException();
    }

    public void StopListening()
    {
        throw new NotImplementedException();
    }
}
