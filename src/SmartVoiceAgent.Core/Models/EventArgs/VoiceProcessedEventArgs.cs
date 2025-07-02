using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Models;

namespace SmartVoiceAgent.Core.EventArgs;

public class VoiceProcessedEventArgs : System.EventArgs
{
    public IntentResult Intent { get; set; }
    public SpeechResult Speech { get; set; }
    public LanguageResult Language { get; set; }
    public TimeSpan TotalProcessingTime { get; set; }
    public Guid SessionId { get; set; }
}
