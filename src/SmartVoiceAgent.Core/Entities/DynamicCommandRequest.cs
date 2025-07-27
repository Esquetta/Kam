namespace SmartVoiceAgent.Core.Entities;
public class DynamicCommandRequest
{
    public string Intent { get; set; }
    public Dictionary<string, object> Entities { get; set; }
    public string OriginalText { get; set; }
    public string Language { get; set; }
    public string Context { get; set; }
}