namespace SmartVoiceAgent.Core.Models.Agents;

/// <summary>
/// How much the agent may do without asking, mirroring the ask / auto-edit / full-auto modes of coding agents.
/// </summary>
public enum ApprovalMode
{
    /// <summary>Read and network-read tools run; everything else asks.</summary>
    Ask = 0,

    /// <summary>File edits run without asking; commands and external actions still ask.</summary>
    AutoEdit = 1,

    /// <summary>Every tool runs without asking.</summary>
    FullAuto = 2
}

/// <summary>
/// The outcome of checking a tool call against the approval mode and saved rules.
/// </summary>
public enum ToolPermissionDecision
{
    /// <summary>Run the call.</summary>
    Allow = 0,

    /// <summary>Ask the user before running the call.</summary>
    Ask = 1,

    /// <summary>Refuse the call.</summary>
    Deny = 2
}
