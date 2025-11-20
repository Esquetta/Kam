namespace SmartVoiceAgent.Core.Dtos.Screen;

public record ScreenContext
{
    // monitor / screen metadata
    public int ScreenIndex { get; init; }
    public string DeviceName { get; init; }
    public bool IsPrimary { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    // active window (nullable for non-primary monitors)
    public ActiveWindowInfo? ActiveWindow { get; init; }

    // analysis results
    public List<OcrLine> OcrLines { get; init; } = new();
    public List<ObjectDetectionItem> Objects { get; init; } = new();

    // content metadata
    public string ScreenshotHash { get; init; }
    public DateTimeOffset Timestamp { get; init; }

    // normalized rectangle representing the captured area in local monitor coordinates (0..1)
    // currently this will be full-screen (0,0,1,1) for the frames we capture; if you add
    // region captures you can populate this accordingly.
    public NormalizedRectangle NormalizedArea { get; init; } = new() { X = 0, Y = 0, Width = 1, Height = 1 };
}
