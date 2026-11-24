using System.Diagnostics;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;

namespace SmartVoiceAgent.Infrastructure.Agent.Evaluation;

/// <summary>
/// Runs one task through the real agent loop in a fresh sandbox and checks the workspace afterwards.
/// </summary>
public static class AgentTaskEvaluator
{
    /// <summary>
    /// Runs a task.
    /// </summary>
    /// <param name="runtime">An agent runtime whose approval gate and workspace root are <paramref name="sandbox"/>.</param>
    /// <param name="sandbox">The sandbox the runtime uses.</param>
    /// <param name="model">Model name for the report.</param>
    /// <param name="task">The task.</param>
    /// <param name="timeout">Longest a turn may take.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    public static async Task<AgentTaskAttempt> RunAsync(
        IAgentRuntime runtime,
        AgentTaskSandbox sandbox,
        string model,
        AgentTaskCase task,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var root = sandbox.Begin(task.Files);
        try
        {
            using var turnTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            turnTimeout.CancelAfter(timeout);

            var tools = new List<string>();
            var failedToolCalls = 0;
            var finalText = string.Empty;
            var error = string.Empty;
            long inputTokens = 0;
            long outputTokens = 0;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await foreach (var agentEvent in runtime.RunTurnAsync($"eval-{Guid.NewGuid():N}", task.Request, turnTimeout.Token))
                {
                    switch (agentEvent)
                    {
                        case AgentToolCallStarted started:
                            tools.Add(started.ToolName);
                            break;
                        case AgentToolCallCompleted { Success: false } completed
                            when !completed.Summary.StartsWith("Blocked", StringComparison.Ordinal):
                            failedToolCalls++;
                            break;
                        case AgentTurnCompleted completed:
                            finalText = completed.FinalText;
                            inputTokens = completed.InputTokens;
                            outputTokens = completed.OutputTokens;
                            break;
                        case AgentTurnFailed failed:
                            error = failed.Message;
                            break;
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                error = $"Timed out after {timeout.TotalSeconds:0} s.";
            }

            stopwatch.Stop();
            var failedChecks = task.Checks
                .Where(check => !Passes(root, check, finalText))
                .Select(check => check.ToString())
                .ToList();

            return new AgentTaskAttempt
            {
                CaseId = task.Id,
                Language = task.Language,
                Model = model,
                Passed = failedChecks.Count == 0,
                FailedChecks = failedChecks,
                ToolCalls = tools.Count,
                FailedToolCalls = failedToolCalls,
                BlockedToolCalls = sandbox.BlockedCalls,
                Blocked = sandbox.BlockedDetails,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                DurationMilliseconds = stopwatch.ElapsedMilliseconds,
                ReplyInRequestLanguage = ReplyMatchesLanguage(task.Language, finalText),
                Error = OneLine(error),
                FinalText = OneLine(finalText),
                Tools = tools
            };
        }
        finally
        {
            sandbox.End();
        }
    }

    /// <summary>
    /// Evaluates one check against a workspace and the agent's final reply.
    /// </summary>
    /// <param name="root">The workspace.</param>
    /// <param name="check">The check.</param>
    /// <param name="finalText">The agent's final reply.</param>
    public static bool Passes(string root, AgentTaskCheck check, string finalText)
    {
        if (check.Type == AgentTaskCheckType.AnswerContains)
        {
            return Normalize(finalText).Contains(Normalize(check.Text), StringComparison.Ordinal);
        }

        var path = AgentTaskSandbox.Resolve(root, check.Path);
        return check.Type switch
        {
            AgentTaskCheckType.Exists => File.Exists(path),
            AgentTaskCheckType.Missing => !File.Exists(path) && !Directory.Exists(path),
            AgentTaskCheckType.Contains => File.Exists(path)
                && Normalize(File.ReadAllText(path)).Contains(Normalize(check.Text), StringComparison.Ordinal),
            AgentTaskCheckType.NotContains => File.Exists(path)
                && !Normalize(File.ReadAllText(path)).Contains(Normalize(check.Text), StringComparison.Ordinal),
            AgentTaskCheckType.Equals => File.Exists(path)
                && Normalize(File.ReadAllText(path)) == Normalize(check.Text),
            _ => false
        };
    }

    /// <summary>
    /// True when <paramref name="reply"/> looks like it is in <paramref name="language"/> ("en" or "tr"):
    /// Turkish replies use Turkish letters, English ones do not.
    /// </summary>
    /// <param name="language">The request's language.</param>
    /// <param name="reply">The agent's final reply.</param>
    public static bool ReplyMatchesLanguage(string language, string reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return false;
        }

        var turkish = reply.Any(character => "çğıöşüÇĞİÖŞÜ".Contains(character));
        return language == "tr" ? turkish : !turkish;
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim().ToLowerInvariant();
    }

    private static string OneLine(string value)
    {
        var line = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return line.Length <= 300 ? line : line[..300] + "...";
    }
}
