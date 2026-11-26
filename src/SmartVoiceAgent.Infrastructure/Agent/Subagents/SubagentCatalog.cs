using SmartVoiceAgent.Core.Models.Agents;

namespace SmartVoiceAgent.Infrastructure.Agent.Subagents;

/// <summary>
/// A kind of subagent the main agent can start: what it is for, its instructions and which tools it gets.
/// </summary>
/// <param name="Name">The type name the model passes, such as "explorer".</param>
/// <param name="Description">When to use it, shown to the model in the <c>task</c> tool.</param>
/// <param name="Instructions">Added to the subagent's system prompt.</param>
/// <param name="ReadOnly">
/// Whether it only gets tools that read (local reads, and web reads for <see cref="ToolRisk.Network"/>). Read-only
/// subagents never ask for approval, so several of them run at the same time.
/// </param>
/// <param name="AllowedRisks">The tool risks it may use; null allows every risk.</param>
public sealed record SubagentDefinition(
    string Name,
    string Description,
    string Instructions,
    bool ReadOnly,
    IReadOnlySet<ToolRisk>? AllowedRisks)
{
    /// <summary>Gets whether the subagent may use <paramref name="tool"/>. Subagents never get the <c>task</c> tool.</summary>
    public bool Allows(AgentToolDescriptor tool) =>
        tool.Risk != ToolRisk.Agent && (AllowedRisks is null || AllowedRisks.Contains(tool.Risk));
}

/// <summary>
/// Kam's built-in subagent types.
/// </summary>
public static class SubagentCatalog
{
    /// <summary>Reads files and local state only.</summary>
    public static readonly SubagentDefinition Explorer = new(
        "explorer",
        "Read-only search of files, folders and local state: find where something is, read and compare files, answer a question about the workspace. Fast, never asks for approval, and several run at the same time.",
        """
        You are an explorer: you only read. Search broadly first, then read the files that matter.
        Report what you found with exact paths (and line numbers when useful), and say what you could not find.
        """,
        ReadOnly: true,
        new HashSet<ToolRisk> { ToolRisk.Read });

    /// <summary>Reads local state and the web.</summary>
    public static readonly SubagentDefinition Researcher = new(
        "researcher",
        "Read-only research on the web and in local files: search, open pages, compare sources and report with links. Never asks for approval, and several run at the same time.",
        """
        You are a researcher: you only read, locally and on the web. Prefer primary sources and check claims in more than one place.
        Report the answer first, then the evidence with links, and say what stayed uncertain.
        """,
        ReadOnly: true,
        new HashSet<ToolRisk> { ToolRisk.Read, ToolRisk.Network });

    /// <summary>Gets every tool except starting subagents.</summary>
    public static readonly SubagentDefinition General = new(
        "general",
        "A self-contained piece of work that may change things: edit files, run commands, use apps or integrations. Its risky calls ask the user for approval like yours, and it runs alone, not next to other subagents.",
        """
        You are a general-purpose worker. Do the whole job you were given, check the result, and stay inside its scope.
        Report what you changed (files, commands, settings), what you verified and anything that failed or was declined.
        """,
        ReadOnly: false,
        AllowedRisks: null);

    /// <summary>Gets the built-in types in the order the tool lists them.</summary>
    public static IReadOnlyList<SubagentDefinition> All { get; } = [Explorer, Researcher, General];

    /// <summary>Finds a type by name, ignoring case, or returns null.</summary>
    /// <param name="name">The type name.</param>
    public static SubagentDefinition? Find(string? name) =>
        All.FirstOrDefault(definition => string.Equals(definition.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
}
