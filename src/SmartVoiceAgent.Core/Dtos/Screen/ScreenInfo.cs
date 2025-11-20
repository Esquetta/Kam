using System.Drawing;

namespace SmartVoiceAgent.Core.Dtos;
public record ScreenInfo
{
    public int Index { get; init; }
    public string DeviceName { get; init; }
    public Rectangle Bounds { get; init; }
    public Rectangle WorkingArea { get; init; }
    public bool IsPrimary { get; init; }
    public int BitsPerPixel { get; init; }
}