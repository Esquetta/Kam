namespace SmartVoiceAgent.Infrastructure.Agent.Conf;

public class AgentConfiguration
{
    public string Provider { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public float Temperature { get; set; } = 0.7f;
    public int MaxTokens { get; set; } = 2000;
    public List<AgentDefinition> Agents { get; set; } = new();
}
