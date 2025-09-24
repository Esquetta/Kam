namespace SmartVoiceAgent.Core.Dtos;
public record ScreenContext
{
    public ActiveWindowInfo ActiveWindow { get; init; }
    public List<OcrLine> OcrLines { get; init; } = new();
    public List<ObjectDetectionItem> Objects { get; init; } = new();
    public string ScreenshotHash { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}
