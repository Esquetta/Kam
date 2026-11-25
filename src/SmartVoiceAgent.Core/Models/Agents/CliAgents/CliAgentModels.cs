namespace SmartVoiceAgent.Core.Models.Agents.CliAgents;

/// <summary>
/// An official coding-agent CLI that Kam can hand tasks to.
/// </summary>
public enum CliAgentKind
{
    /// <summary>OpenAI's Codex CLI (<c>codex</c>).</summary>
    Codex = 0,

    /// <summary>Anthropic's Claude Code CLI (<c>claude</c>).</summary>
    ClaudeCode = 1
}

/// <summary>
/// How a CLI is signed in, as the CLI itself reports it.
/// </summary>
public enum CliAgentSignIn
{
    /// <summary>The CLI did not say.</summary>
    Unknown = 0,

    /// <summary>The CLI is not signed in.</summary>
    SignedOut = 1,

    /// <summary>Signed in with a ChatGPT or Claude plan, so work bills to that plan.</summary>
    Subscription = 2,

    /// <summary>Signed in with an API key, so work bills to the API account.</summary>
    ApiKey = 3
}

/// <summary>
/// What Kam found out about one CLI. Kam asks the CLI; it never reads the CLI's credential files.
/// </summary>
/// <param name="Kind">Which CLI.</param>
/// <param name="IsInstalled">Whether the CLI is on PATH.</param>
/// <param name="Version">The version the CLI reports, such as <c>0.161.0</c>.</param>
/// <param name="SignIn">How the CLI is signed in.</param>
/// <param name="Plan">The plan name the CLI reports, such as <c>max</c>, when it reports one.</param>
public sealed record CliAgentStatus(
    CliAgentKind Kind,
    bool IsInstalled,
    string? Version,
    CliAgentSignIn SignIn,
    string? Plan = null);

/// <summary>
/// Settings for handing tasks to coding-agent CLIs, from the <c>CliAgents</c> configuration section.
/// </summary>
public sealed class CliAgentsOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "CliAgents";

    /// <summary>Gets or sets whether the agent may hand tasks to the Codex CLI.</summary>
    public bool CodexEnabled { get; set; }

    /// <summary>Gets or sets how long one delegated task may run.</summary>
    public int TimeoutMinutes { get; set; } = 20;
}
