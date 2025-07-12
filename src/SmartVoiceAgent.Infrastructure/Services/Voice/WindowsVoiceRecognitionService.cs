using NAudio.Wave;
using SmartVoiceAgent.Infrastructure.Services.Voice;

public class WindowsVoiceRecognitionService : VoiceRecognitionServiceBase
{
    private WaveInEvent _waveIn;

    protected override void StartListeningInternal()
    {
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1), // 16-bit, mono, 16kHz
            BufferMilliseconds = 100
        };

        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStoppedHandler;
        _waveIn.StartRecording();
    }

    protected override void StopListeningInternal()
    {
        _waveIn?.StopRecording();
    }

    protected override void CleanupPlatformResources()
    {
        if (_waveIn != null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStoppedHandler;
            _waveIn.Dispose();
            _waveIn = null;
        }
    }

    private void OnDataAvailable(object sender, WaveInEventArgs e)
    {
        try
        {
            if (e.BytesRecorded > 0)
            {
                var buffer = new byte[e.BytesRecorded];
                Array.Copy(e.Buffer, buffer, e.BytesRecorded);
                AddAudioData(buffer);
            }
        }
        catch (Exception ex)
        {
            InvokeOnError(ex);
        }
    }

    private void OnRecordingStoppedHandler(object sender, StoppedEventArgs e)
    {
        try
        {
            if (e.Exception != null)
            {
                InvokeOnError(e.Exception);
                return;
            }

            var audioData = GetAndClearAudioData();
            OnListeningComplete(audioData);
        }
        catch (Exception ex)
        {
            InvokeOnError(ex);
        }
    }
}
