namespace SmartVoiceAgent.Ui.ViewModels.PageModels;

public sealed class RuntimeDiagnosticItemViewModel
{
    public RuntimeDiagnosticItemViewModel(
        string name,
        string value,
        string detail,
        RuntimeDiagnosticSeverity severity)
    {
        Name = name;
        Value = value;
        Detail = detail;
        Severity = severity;
    }

    public string Name { get; }

    public string Value { get; }

    public string Detail { get; }

    public RuntimeDiagnosticSeverity Severity { get; }

    public bool IsReady => Severity == RuntimeDiagnosticSeverity.Ready;

    public bool IsWarning => Severity == RuntimeDiagnosticSeverity.Warning;

    public bool IsBlocked => Severity == RuntimeDiagnosticSeverity.Blocked;

    public bool IsNeutral => Severity == RuntimeDiagnosticSeverity.Neutral;
}

public enum RuntimeDiagnosticSeverity
{
    Ready = 0,
    Warning = 1,
    Blocked = 2,
    Neutral = 3
}
