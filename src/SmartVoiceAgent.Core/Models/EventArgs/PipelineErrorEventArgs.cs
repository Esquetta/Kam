namespace SmartVoiceAgent.Core.EventArgs;

public class PipelineErrorEventArgs : System.EventArgs
{
    public Exception Exception { get; set; }
    public string Stage { get; set; } // "STT", "LanguageDetection", "IntentDetection"
    public string Context { get; set; }
    public Guid SessionId { get; set; }
}
