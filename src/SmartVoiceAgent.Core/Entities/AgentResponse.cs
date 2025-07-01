namespace SmartVoiceAgent.Core.Entities;

public class AgentResponse
{
    public string Response { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();
    public bool RequiresFollowUp { get; set; }
    public List<string> SuggestedActions { get; set; } = new();
}
