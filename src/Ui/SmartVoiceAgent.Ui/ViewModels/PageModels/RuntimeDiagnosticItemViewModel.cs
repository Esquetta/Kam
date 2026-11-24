namespace SmartVoiceAgent.Ui.ViewModels.PageModels;

public sealed class RuntimeDiagnosticItemViewModel
{
    public RuntimeDiagnosticItemViewModel(
        string name,
        string value,
        string detail,
        RuntimeDiagnosticSeverity severity,
        string actionText = "",
        string actionTarget = "")
    {
        Name = name;
        Value = value;
        Detail = detail;
        Severity = severity;
        ActionText = actionText;
        ActionTarget = actionTarget;
    }

    public string Name { get; }

    public string Value { get; }

    public string Detail { get; }

    public RuntimeDiagnosticSeverity Severity { get; }

    /// <summary>
    /// Gets the label of the one action offered on a status card, empty when there is none.
    /// </summary>
    public string ActionText { get; }

    /// <summary>
    /// Gets the page the card action opens: <c>Settings</c>, <c>Skills</c> or <c>Extensions</c>.
    /// </summary>
    public string ActionTarget { get; }

    public bool HasAction => !string.IsNullOrEmpty(ActionText);

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
