namespace SmartVoiceAgent.Core.Entities;
public class AvailableCommand
{
    public string Intent { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Examples { get; set; } = [];
    public string Category { get; set; } = string.Empty;
}
