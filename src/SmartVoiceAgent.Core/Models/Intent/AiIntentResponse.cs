namespace SmartVoiceAgent.Core.Models.Intent;
public class AiIntentResponse
{
    public string Intent { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public Dictionary<string, object> Entities { get; set; } = [];
    public string Reasoning { get; set; } = string.Empty;
}
