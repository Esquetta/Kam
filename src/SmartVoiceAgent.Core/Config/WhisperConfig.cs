namespace SmartVoiceAgent.Core.Config;

public class WhisperConfig
{
    public int MaxConcurrentRequests { get; set; } = 2;
    public string ModelPath { get; set; } = "ggml-base.bin";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
