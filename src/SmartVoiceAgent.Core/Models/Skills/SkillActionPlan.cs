namespace SmartVoiceAgent.Core.Models.Skills;

public static class SkillActionTypes
{
    public const string Respond = "respond";
    public const string OpenApp = "open_app";
    public const string FocusWindow = "focus_window";
    public const string Click = "click";
    public const string TypeText = "type_text";
    public const string Hotkey = "hotkey";
    public const string ClipboardSet = "clipboard_set";
    public const string ClipboardGet = "clipboard_get";
    public const string ReadScreen = "read_screen";
}

public sealed class SkillActionPlan
{
    public string Message { get; set; } = string.Empty;

    public bool RequiresConfirmation { get; set; }

    public List<SkillActionStep> Actions { get; set; } = [];
}

public sealed class SkillActionStep
{
    public string Type { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public string ApplicationName { get; set; } = string.Empty;

    public string WindowTitle { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public List<string> Keys { get; set; } = [];

    public int? X { get; set; }

    public int? Y { get; set; }

    public int TimeoutMilliseconds { get; set; }
}

public sealed record SkillActionExecutionResult(
    bool Success,
    string Message,
    IReadOnlyList<SkillActionStepResult> Steps)
{
    public static SkillActionExecutionResult Succeeded(
        string message,
        IReadOnlyList<SkillActionStepResult> steps) =>
        new(true, message, steps);

    public static SkillActionExecutionResult Failed(
        string message,
        IReadOnlyList<SkillActionStepResult> steps) =>
        new(false, message, steps);
}

public sealed record SkillActionStepResult(
    bool Success,
    string ActionType,
    string Message,
    string ErrorCode = "")
{
    public static SkillActionStepResult Succeeded(string actionType, string message) =>
        new(true, actionType, message);

    public static SkillActionStepResult Failed(string actionType, string message, string errorCode = "") =>
        new(false, actionType, message, errorCode);
}
