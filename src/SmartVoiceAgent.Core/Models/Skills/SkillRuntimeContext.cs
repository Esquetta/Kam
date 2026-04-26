namespace SmartVoiceAgent.Core.Models.Skills;

public sealed class SkillRuntimeContext
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public string UserInput { get; set; } = string.Empty;

    public string OperatingSystem { get; set; } = string.Empty;

    public SkillRuntimeWindow? ActiveWindow { get; set; }

    public List<SkillRuntimeWindow> VisibleWindows { get; set; } = [];

    public List<SkillRuntimeScreenContext> Screens { get; set; } = [];

    public List<string> InstalledApplications { get; set; } = [];
}

public sealed class SkillRuntimeWindow
{
    public string Title { get; set; } = string.Empty;

    public string ProcessName { get; set; } = string.Empty;

    public int ProcessId { get; set; }

    public bool HasFocus { get; set; }

    public bool IsVisible { get; set; }
}

public sealed class SkillRuntimeScreenContext
{
    public int ScreenIndex { get; set; }

    public string DeviceName { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public List<string> OcrLines { get; set; } = [];
}
