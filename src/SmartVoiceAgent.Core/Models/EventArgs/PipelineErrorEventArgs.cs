namespace SmartVoiceAgent.Core.EventArgs;

public class PipelineErrorEventArgs : System.EventArgs
{
    public Exception Exception { get; set; } = new InvalidOperationException("Pipeline error has not been initialized.");
    public string Stage { get; set; } = string.Empty; // "STT", "LanguageDetection", "IntentDetection"
    public string Context { get; set; } = string.Empty;
    public Guid SessionId { get; set; }
}
