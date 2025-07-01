
namespace SmartVoiceAgent.Core.Config;
public class VoiceRecognitionConfig
{
    public int SampleRate { get; set; } = 16000;
    public int BitsPerSample { get; set; } = 16;
    public int Channels { get; set; } = 1;
    public int BufferMilliseconds { get; set; } = 100;
}
