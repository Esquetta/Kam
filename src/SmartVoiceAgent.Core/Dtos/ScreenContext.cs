namespace SmartVoiceAgent.Core.Dtos;

public record ScreenContext
{
    /// <summary>
    /// Index of the captured monitor (0 = primary).
    /// </summary>
    public int ScreenIndex { get; init; }

    /// <summary>
    /// Device name for the monitor (e.g. "Monitor1", "Monitor2").
    /// </summary>
    public string DeviceName { get; init; }

    /// <summary>
    /// True if this is the primary monitor.
    /// </summary>
    public bool IsPrimary { get; init; }

    /// <summary>
    /// Width of the captured screen in pixels.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Height of the captured screen in pixels.
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// Currently active window (only populated for primary monitor).
    /// </summary>
    public ActiveWindowInfo? ActiveWindow { get; init; }

    /// <summary>
    /// OCR text lines detected from this monitor.
    /// </summary>
    public List<OcrLine> OcrLines { get; init; } = new();

    /// <summary>
    /// Detected objects (UI buttons, icons, shapes, etc.)
    /// </summary>
    public List<ObjectDetectionItem> Objects { get; init; } = new();

    /// <summary>
    /// Hash of the screenshot (used for duplicate detection).
    /// </summary>
    public string ScreenshotHash { get; init; }

    /// <summary>
    /// Timestamp of capture.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }
}
