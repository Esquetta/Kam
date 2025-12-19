namespace SmartVoiceAgent.Infrastructure.Agent.Conf;

public class AIServiceConfiguration
{
    public string Provider { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public float DefaultTemperature { get; set; } = 0.7f;
    public int DefaultMaxTokens { get; set; } = 2000;
}
