using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Models.Audio;

namespace SmartVoiceAgent.Core.EventArgs;

public class VoiceProcessedEventArgs : System.EventArgs
{
    public IntentResult Intent { get; set; } = new();
    public SpeechResult Speech { get; set; } = new();
    public LanguageResult Language { get; set; } = new();
    public TimeSpan TotalProcessingTime { get; set; }
    public Guid SessionId { get; set; }
}
