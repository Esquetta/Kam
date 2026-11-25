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

    /// <summary>
    /// Gets or sets how many times one model request is sent again after a temporary error
    /// (timeout, 429, 5xx, dropped stream). 0 turns retries off.
    /// </summary>
    public int MaxModelRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the wait before the first retry, in milliseconds. Each further retry waits twice as
    /// long, up to 30 seconds.
    /// </summary>
    public int ModelRetryBaseDelayMilliseconds { get; set; } = 2000;

    /// <summary>
    /// Gets or sets how many read-only tool calls from one model response may run at the same time.
    /// 1 runs every call one after another.
    /// </summary>
    public int MaxParallelToolCalls { get; set; } = 8;

    /// <summary>Clamps a step limit to 1-200.</summary>
    /// <param name="value">The requested limit.</param>
    public static int ClampMaxIterations(int value) => Math.Clamp(value, 1, 200);

    /// <summary>Clamps a tool result length to 1000-200000 characters.</summary>
    /// <param name="value">The requested length.</param>
    public static int ClampMaxToolResultCharacters(int value) => Math.Clamp(value, 1000, 200000);

    /// <summary>Clamps a context budget to 0 (compaction off) or 8000-1000000 tokens.</summary>
    /// <param name="value">The requested budget.</param>
    public static int ClampContextTokenBudget(int value) => value <= 0 ? 0 : Math.Clamp(value, 8000, 1000000);
}
