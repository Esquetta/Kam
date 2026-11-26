using SmartVoiceAgent.Core.Models.Agents;

namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Keeps the subagent runs the agent runtime starts, live while they run and saved once they end,
/// so the chat, Runs and Graphs pages can follow them.
/// </summary>
public interface ISubagentRunTracker
{
    /// <summary>Raised with the new state of a run whenever it starts, changes or ends. May fire on any thread.</summary>
    event EventHandler<SubagentRun>? Changed;

    /// <summary>
    /// Lists runs, newest first.
    /// </summary>
    /// <param name="parentSessionId">Only the runs started from this task; null lists every task's runs.</param>
    /// <param name="maxCount">The most runs to return.</param>
    IReadOnlyList<SubagentRun> List(string? parentSessionId = null, int maxCount = 200);

    /// <summary>Gets a run, or null when it is not known.</summary>
    SubagentRun? Get(string runId);

    /// <summary>
    /// Stops a running subagent. The main agent receives a note that the user stopped it and its turn goes on.
    /// </summary>
    /// <returns><c>true</c> when the run was still running.</returns>
    bool Cancel(string runId);

    /// <summary>Records a new run. Called by the agent runtime.</summary>
    /// <param name="run">The run, with status <see cref="SubagentRunStatus.Running"/>.</param>
    /// <param name="cancellation">Stops the run when <see cref="Cancel"/> is called.</param>
    void Start(SubagentRun run, CancellationTokenSource cancellation);

    /// <summary>Changes a running run. Called by the agent runtime.</summary>
    /// <param name="runId">The run.</param>
    /// <param name="change">Returns the new state from the current one.</param>
    void Update(string runId, Func<SubagentRun, SubagentRun> change);

    /// <summary>Ends a run and saves it. Called by the agent runtime.</summary>
    /// <param name="runId">The run.</param>
    /// <param name="change">Returns the final state from the current one.</param>
    void Finish(string runId, Func<SubagentRun, SubagentRun> change);
}
