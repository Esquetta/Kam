

using SmartVoiceAgent.Core.Enums;

namespace SmartVoiceAgent.Core.Entities;

public class IntentResult
{
    public CommandType Intent { get; set; }
    public float Confidence { get; set; }
    public Dictionary<string, object> Entities { get; set; } = new();
    public string Language { get; set; } = "en";
    public string OriginalText { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Guid SessionId { get; set; } = Guid.NewGuid();
}
