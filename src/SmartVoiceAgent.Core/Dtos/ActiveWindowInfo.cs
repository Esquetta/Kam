namespace SmartVoiceAgent.Core.Dtos;
public record ActiveWindowInfo
{
    public string Title { get; init; }
    public int ProcessId { get; init; }
    public string ProcessName { get; init; }
    public string ExecutablePath { get; init; }
}