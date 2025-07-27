namespace SmartVoiceAgent.Core.Entities;
public class AvailableCommand
{
    public string Intent { get; set; }
    public string Description { get; set; }
    public List<string> Examples { get; set; }
    public string Category { get; set; }
}
