namespace SmartVoiceAgent.Core.Models.Agents;

/// <summary>
/// What a tool can change, used to decide whether a call needs the user's approval.
/// </summary>
public enum ToolRisk
{
    /// <summary>Reads local state only.</summary>
    Read = 0,

    /// <summary>Changes files inside the workspace or local app state.</summary>
    Write = 1,

    /// <summary>Runs commands or controls processes and devices.</summary>
    Execute = 2,

    /// <summary>Reads from the network without side effects.</summary>
    Network = 3,

    /// <summary>Acts outside the machine, such as sending email or calling a third-party API.</summary>
    External = 4,

    /// <summary>
    /// Hands the task to another agent, such as the user's Codex CLI, whose own actions Kam cannot see or gate.
    /// Always asks for approval; only deny rules apply.
    /// </summary>
    Delegate = 5
}
