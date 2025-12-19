namespace SmartVoiceAgent.Infrastructure.Agent.Conf;

public class AgentDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public List<string> Tools { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public Dictionary<string, object> Metadata { get; set; } = new();
}