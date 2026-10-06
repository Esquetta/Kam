namespace SmartVoiceAgent.Core.Models.Agents.Extensions;

/// <summary>
/// An Agent Skill: a folder with a <c>SKILL.md</c> whose instructions the agent loads when a task matches.
/// </summary>
/// <param name="Name">The name the agent loads it by.</param>
/// <param name="Description">When to use the skill; this is all the model sees until it loads the skill.</param>
/// <param name="Directory">The skill folder.</param>
/// <param name="Source">Where it was found: <c>user</c>, <c>workspace</c>, <c>imported</c> or <c>plugin:{name}</c>.</param>
public sealed record AgentSkillInfo(string Name, string Description, string Directory, string Source)
{
    /// <summary>Gets the instructions file.</summary>
    public string SkillFile => Path.Combine(Directory, "SKILL.md");
}

/// <summary>
/// A slash command written as a Markdown prompt, such as <c>commands/review.md</c>.
/// </summary>
/// <param name="Name">The command without the slash.</param>
/// <param name="Description">What the command does.</param>
/// <param name="ArgumentHint">What to type after the command, if anything.</param>
/// <param name="FilePath">The Markdown file.</param>
/// <param name="Source">Where it was found: <c>user</c>, <c>workspace</c> or <c>plugin:{name}</c>.</param>
public sealed record AgentCommandInfo(string Name, string Description, string ArgumentHint, string FilePath, string Source);

/// <summary>
/// An installed plugin in the Claude Code layout: skills, commands, agents and MCP servers in one folder.
/// </summary>
public sealed record AgentPluginInfo
{
    /// <summary>Gets the plugin name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the version from the manifest.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Gets the description from the manifest.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the author from the manifest.</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>Gets the plugin folder.</summary>
    public required string Directory { get; init; }

    /// <summary>Gets whether the plugin contributes to the agent.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets the skill folders the plugin contributes.</summary>
    public IReadOnlyList<string> SkillDirectories { get; init; } = [];

    /// <summary>Gets the command files the plugin contributes.</summary>
    public IReadOnlyList<string> CommandFiles { get; init; } = [];

    /// <summary>Gets the number of subagent definitions; they run once subagents arrive.</summary>
    public int AgentCount { get; init; }

    /// <summary>Gets the MCP servers the plugin contributes, with its folder already filled in.</summary>
    public IReadOnlyList<McpServerDefinition> McpServers { get; init; } = [];

    /// <summary>Gets a problem with the plugin's files, if any.</summary>
    public string? Error { get; init; }
}

/// <summary>
/// The outcome of installing plugins.
/// </summary>
/// <param name="Success">Whether at least one plugin was installed.</param>
/// <param name="Message">A sentence for the user.</param>
/// <param name="Installed">The names of the installed plugins.</param>
public sealed record AgentPluginInstallResult(bool Success, string Message, IReadOnlyList<string> Installed);
