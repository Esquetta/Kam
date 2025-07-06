namespace SmartVoiceAgent.Core.Config;

public class WhisperConfig
{
    public int MaxConcurrentRequests { get; set; } = 2;
    public string ModelPath { get; set; } = "ggml-base.bin";
    public string Language { get; set; } = "auto";
    public int BeamSize { get; set; } = 1;
}

