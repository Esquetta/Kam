namespace SmartVoiceAgent.Core.Models.Agents;

/// <summary>
/// Settings for the tool-calling agent runtime (configuration section "AgentRuntime").
/// </summary>
public sealed class AgentRuntimeOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "AgentRuntime";

    /// <summary>Gets or sets whether chat uses the agent loop instead of the legacy single-skill planner.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the maximum number of model round trips in one turn.</summary>
    public int MaxIterations { get; set; } = 24;

    /// <summary>Gets or sets the default approval mode for new sessions.</summary>
    public ApprovalMode ApprovalMode { get; set; } = ApprovalMode.Ask;

    /// <summary>Gets or sets the longest tool result, in characters, sent back to the model.</summary>
    public int MaxToolResultCharacters { get; set; } = 16000;

    /// <summary>
    /// Gets or sets the estimated tokens a request may use before older tool results are shortened and
    /// earlier turns are summarized. 0 turns compaction off.
    /// </summary>
    public int ContextTokenBudget { get; set; } = 64000;
}
