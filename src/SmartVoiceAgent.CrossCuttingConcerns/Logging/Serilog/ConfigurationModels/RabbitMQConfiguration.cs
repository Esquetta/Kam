namespace Core.CrossCuttingConcerns.Logging.Serilog.ConfigurationModels;

public class RabbitMQConfiguration
{
    public int Port { get; set; }
    public string Exchange { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ExchangeType { get; set; } = string.Empty;
    public string RouteKey { get; set; } = string.Empty;
    public List<string> Hostnames { get; set; } = [];
}
