using NAudio.Wave;
using SmartVoiceAgent.Core.Interfaces;

public class WindowsVoiceRecognitionService : IVoiceRecognitionService
{
    private WaveInEvent _waveIn;
    private MemoryStream _memoryStream;

    public event EventHandler<byte[]> OnVoiceCaptured;

    public void StartRecording()
    {
        _memoryStream = new MemoryStream();
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 1)
        };

        _waveIn.DataAvailable += (s, a) =>
        {
            _memoryStream.Write(a.Buffer, 0, a.BytesRecorded);
        };

        _waveIn.RecordingStopped += (s, a) =>
        {
            var audioData = _memoryStream.ToArray();
            OnVoiceCaptured?.Invoke(this, audioData);
            _memoryStream.Dispose();
            _waveIn.Dispose();
        };

        _waveIn.StartRecording();
    }

    public void StopRecording()
    {
        _waveIn?.StopRecording();
    }
}
