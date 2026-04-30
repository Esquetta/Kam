namespace SmartVoiceAgent.Core.Entities;
public class DynamicCommandRequest
{
    public string Intent { get; set; } = string.Empty;
    public Dictionary<string, object> Entities { get; set; } = [];
    public string OriginalText { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
}
