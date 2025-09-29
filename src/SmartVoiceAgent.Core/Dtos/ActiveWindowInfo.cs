using System.Drawing;

namespace SmartVoiceAgent.Core.Dtos;
public record ActiveWindowInfo
{
    public string Title { get; init; }
    public int ProcessId { get; init; }
    public string ProcessName { get; init; }
    public string ExecutablePath { get; init; }
    public Rectangle WindowBounds { get; init; }
    public bool IsMaximized { get; init; }
    public bool IsMinimized { get; init; }
    public bool IsVisible { get; init; }
}