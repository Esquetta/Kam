namespace SmartVoiceAgent.Core.Dtos.Screen;
public record ScreenCaptureFrame
{
    public byte[] PngImage { get; init; }
    public byte[]? PreviewPng { get; set; }
    public DateTimeOffset Timestamp { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int ScreenIndex { get; init; } 
    public string DeviceName { get; init; }
}