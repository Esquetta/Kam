using System.Drawing;

namespace SmartVoiceAgent.Core.Dtos;
public record ActiveWindowInfo
{
    /// <summary>
    /// Window title text
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Process ID of the window owner
    /// </summary>
    public int ProcessId { get; init; }

    /// <summary>
    /// Process name (e.g., "chrome", "notepad")
    /// </summary>
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>
    /// Full path to the executable
    /// </summary>
    public string ExecutablePath { get; init; } = string.Empty;

    /// <summary>
    /// Window bounds in absolute screen coordinates
    /// </summary>
    public Rectangle WindowBounds { get; init; }

    /// <summary>
    /// Normalized window bounds (0-1 range relative to the screen)
    /// </summary>
    public NormalizedRectangle NormalizedBounds { get; init; } = new();

    /// <summary>
    /// Screen index where the window is primarily located (0-based)
    /// </summary>
    public int ScreenIndex { get; init; }

    /// <summary>
    /// True if window is maximized
    /// </summary>
    public bool IsMaximized { get; init; }

    /// <summary>
    /// True if window is minimized
    /// </summary>
    public bool IsMinimized { get; init; }

    /// <summary>
    /// True if window is visible (not hidden)
    /// </summary>
    public bool IsVisible { get; init; }

    /// <summary>
    /// True if window is in foreground/has focus
    /// </summary>
    public bool HasFocus { get; init; }

    /// <summary>
    /// Window handle (HWND on Windows)
    /// </summary>
    public IntPtr WindowHandle { get; init; }

    /// <summary>
    /// Window class name (useful for identifying window types)
    /// </summary>
    public string ClassName { get; init; } = string.Empty;

    /// <summary>
    /// Timestamp when this window info was captured
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Z-order/layer index (higher = more foreground)
    /// </summary>
    public int ZOrder { get; init; }

    /// <summary>
    /// Window opacity (0.0 - 1.0)
    /// </summary>
    public float Opacity { get; init; } = 1.0f;

    /// <summary>
    /// True if window is always on top
    /// </summary>
    public bool IsAlwaysOnTop { get; init; }

    /// <summary>
    /// True if window is a dialog
    /// </summary>
    public bool IsDialog { get; init; }

    /// <summary>
    /// True if window is a tool window
    /// </summary>
    public bool IsToolWindow { get; init; }

    /// <summary>
    /// Additional metadata (icons, themes, etc.)
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}


