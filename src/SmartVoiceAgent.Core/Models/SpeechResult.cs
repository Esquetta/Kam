namespace SmartVoiceAgent.Core.Models;

public class SpeechResult
{
    public string Text { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public bool IsSuccess => !string.IsNullOrWhiteSpace(Text) && Confidence > 0.3f;
    public string ErrorMessage { get; set; } = string.Empty;
}