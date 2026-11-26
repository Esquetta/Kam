using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Infrastructure.Agent.Subagents;

namespace SmartVoiceAgent.Infrastructure.Agent.Runtime;

/// <summary>
/// Subagents: the <c>task</c> tool runs a separate tool loop with its own context and a subset of the turn's tools.
/// Its tool calls pass the same approval gate, and its activity streams into the parent turn as
/// <see cref="AgentSubagentActivity"/> events while <see cref="ISubagentRunTracker"/> keeps the run for the Runs and Graphs pages.
/// </summary>
public sealed partial class AgentRuntime
{
    /// <summary>
    /// Tells the main agent when to delegate. Models rarely start subagents from the tool description alone,
    /// so the main agent's prompt says it plainly; subagents do not get this section.
    /// </summary>
    internal const string SubagentGuidance =
        """
        ## Subagents
        You can start subagents with the `task` tool. Each works with its own context and sends you a report.
        - Delegate when a request needs a broad search or reading many files, such as "find every place that does X in this project", a review of a folder, or research from several sources. Split it into independent parts and start one explorer (local files) or researcher (web) subagent per part in the same response, so they run at the same time. Then combine their reports into your answer.
        - Use a general subagent for a self-contained change you can describe fully, when you want to keep your own context small.
        - Do it yourself when one or two tool calls will answer it, or when the parts depend on each other.
        - A subagent does not see this conversation: give it the folder, what to look for and what to report.
        """;

    private const string SubagentStoppedResult =
        "Error: the user stopped this subagent before it finished. Continue without its report, or ask the user how to proceed.";

    /// <summary>What a turn's subagents share with it.</summary>
    private sealed record SubagentTurn(
        string SessionId,
        string? ModelId,
        IChatClient ChatClient,
        IReadOnlyList<AgentToolDescriptor> Tools,
        IReadOnlyList<string> PromptSections,
        ChannelWriter<AgentEvent> Events,
        TurnLimits Limits);

    private AgentToolDescriptor CreateSubagentTool(SubagentTurn turn) =>
        new(
            new SubagentTaskFunction((callId, arguments, cancellationToken) => RunSubagentAsync(turn, callId, arguments, cancellationToken)),
            ToolRisk.Agent,
            "builtin",
            "Subagent");

    private async Task<string> RunSubagentAsync(
        SubagentTurn turn,
        string callId,
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var typeName = SubagentTaskFunction.ReadString(arguments, "subagent_type");
        var definition = SubagentCatalog.Find(typeName);
        if (definition is null)
        {
            return $"Error: there is no subagent type '{typeName}'. Use one of: {string.Join(", ", SubagentCatalog.All.Select(item => item.Name))}.";
        }

        var prompt = SubagentTaskFunction.ReadString(arguments, "prompt")?.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return "Error: the prompt is empty. Give the subagent a complete brief.";
        }

        var description = SubagentTaskFunction.ReadString(arguments, "description")?.Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            description = prompt.Length <= 48 ? prompt : prompt[..48] + "...";
        }

        var tools = turn.Tools.Where(definition.Allows).ToList();
        var runId = "sub_" + Guid.NewGuid().ToString("N")[..12];
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _subagents.Start(
            new SubagentRun(runId, turn.SessionId, callId, definition.Name, description, prompt, SubagentRunStatus.Running, _clock(), ModelId: turn.ModelId, Steps: []),
            cancellation);
        await turn.Events.WriteAsync(
            new AgentSubagentStarted(turn.SessionId, callId, runId, definition.Name, description),
            CancellationToken.None);

        var childEvents = Channel.CreateUnbounded<AgentEvent>(new UnboundedChannelOptions { SingleReader = true });
        var pump = Task.Run(() => ForwardSubagentEventsAsync(turn, callId, runId, definition.Name, childEvents.Reader), CancellationToken.None);
        var usage = new TurnUsage();
        var status = SubagentRunStatus.Failed;
        string? error = null;
        string? report = null;

        try
        {
            var systemPrompt = AgentSystemPrompt.Build(
                _clock(),
                tools.Count,
                _workspaceRoot(),
                [.. turn.PromptSections, BuildSubagentSection(definition)]);
            var final = await RunSubagentLoopAsync(runId, prompt, tools, systemPrompt, turn, childEvents.Writer, usage, cancellation.Token);
            if (final is null)
            {
                error = $"Stopped after {turn.Limits.MaxIterations} steps without a report.";
                report = $"Error: the subagent stopped after {turn.Limits.MaxIterations} steps without a report.";
            }
            else
            {
                status = SubagentRunStatus.Completed;
                report = string.IsNullOrWhiteSpace(final) ? "The subagent finished without a report." : final;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Only this subagent was stopped; the parent turn goes on.
            status = SubagentRunStatus.Cancelled;
            error = "Stopped by the user.";
            report = SubagentStoppedResult;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = SubagentRunStatus.Cancelled;
            error = "The task was stopped.";
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subagent {RunId} failed", runId);
            error = ex.Message;
            report = $"Error: the subagent failed: {ex.Message}";
        }
        finally
        {
            childEvents.Writer.TryComplete();
            await pump;
            var completedAt = _clock();
            var finalStatus = status;
            var finalError = error;
            var finalReport = report;
            _subagents.Finish(runId, run => run with
            {
                Status = finalStatus,
                CompletedAt = completedAt,
                InputTokens = usage.InputTokens,
                OutputTokens = usage.OutputTokens,
                Error = finalError,
                Result = finalReport,
                Steps = run.StepList.Select(step => step.Status is SubagentStepStatus.Running or SubagentStepStatus.AwaitingApproval
                    ? step with { Status = SubagentStepStatus.Failed, CompletedAt = completedAt, Summary = "Not run: the subagent stopped." }
                    : step).ToList()
            });
            await turn.Events.WriteAsync(
                new AgentSubagentCompleted(
                    turn.SessionId,
                    callId,
                    runId,
                    finalStatus,
                    _subagents.Get(runId)?.StepList.Count ?? 0,
                    usage.InputTokens,
                    usage.OutputTokens),
                CancellationToken.None);
        }

        return report!;
    }

    /// <summary>
    /// Runs the subagent's own loop on a history that starts with its brief. Returns its final text, or null
    /// when it reached the step limit. Old tool results are shortened to fit the context budget; nothing is summarized.
    /// </summary>
    private async Task<string?> RunSubagentLoopAsync(
        string runId,
        string prompt,
        IReadOnlyList<AgentToolDescriptor> tools,
        string systemPrompt,
        SubagentTurn turn,
        ChannelWriter<AgentEvent> events,
        TurnUsage usage,
        CancellationToken cancellationToken)
    {
        var toolsByName = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var toolTokens = AgentContextWindow.EstimateTokens(tools);
        var history = new List<ChatMessage> { new(ChatRole.User, prompt) };
        var chatOptions = new ChatOptions
        {
            Tools = tools.Select(tool => (AITool)tool.Function).ToList(),
            ToolMode = tools.Count > 0 ? ChatToolMode.Auto : ChatToolMode.None,
            AllowMultipleToolCalls = true
        };

        for (var iteration = 0; iteration < turn.Limits.MaxIterations; iteration++)
        {
            var messages = AgentContextWindow.BuildModelMessages(systemPrompt, history, shortenOldToolResults: false);
            var budget = turn.Limits.ContextTokenBudget;
            if (budget > 0 && toolTokens + AgentContextWindow.EstimateTokens(messages) > budget)
            {
                messages = AgentContextWindow.BuildModelMessages(systemPrompt, history, shortenOldToolResults: true);
            }

            var response = await GetModelResponseAsync(runId, turn.ChatClient, messages, chatOptions, events, cancellationToken);
            usage.Add(response.Usage);
            history.AddRange(response.Messages);
            _subagents.Update(runId, run => run with { InputTokens = usage.InputTokens, OutputTokens = usage.OutputTokens });

            var calls = response.Messages
                .SelectMany(message => message.Contents)
                .OfType<FunctionCallContent>()
                .ToList();
            if (calls.Count == 0)
            {
                return response.Text;
            }

            var results = await RunToolCallsAsync(runId, calls, toolsByName, events, turn.Limits, cancellationToken);
            history.Add(new ChatMessage(ChatRole.Tool, [.. results]));
        }

        return null;
    }

    /// <summary>
    /// Passes the subagent's tool activity on to the parent turn and records it on the run. Text the subagent
    /// streams stays out of the parent's chat; only its report reaches the main agent.
    /// </summary>
    private async Task ForwardSubagentEventsAsync(
        SubagentTurn turn,
        string callId,
        string runId,
        string agentType,
        ChannelReader<AgentEvent> reader)
    {
        await foreach (var agentEvent in reader.ReadAllAsync(CancellationToken.None))
        {
            switch (agentEvent)
            {
                case AgentToolCallStarted started:
                    _subagents.Update(runId, run => run with
                    {
                        Steps = [.. run.StepList, new SubagentStep(started.CallId, started.ToolName, started.DisplayName, started.ArgumentsJson, started.Risk, SubagentStepStatus.Running, _clock())]
                    });
                    break;

                case AgentApprovalRequested approval:
                    UpdateStep(runId, approval.CallId, step => step with { Status = SubagentStepStatus.AwaitingApproval });
                    break;

                case AgentApprovalResolved resolved:
                    // A declined call completes as failed right after; an approved one runs now.
                    if (resolved.Approved)
                    {
                        UpdateStep(runId, resolved.CallId, step => step with { Status = SubagentStepStatus.Running });
                    }

                    break;

                case AgentToolCallCompleted completed:
                    UpdateStep(runId, completed.CallId, step => step with
                    {
                        Status = completed.Success ? SubagentStepStatus.Succeeded : SubagentStepStatus.Failed,
                        CompletedAt = _clock(),
                        Summary = completed.Summary
                    });
                    break;

                case AgentModelRetryScheduled:
                    break;

                default:
                    continue;
            }

            await turn.Events.WriteAsync(
                new AgentSubagentActivity(turn.SessionId, callId, runId, agentType, agentEvent),
                CancellationToken.None);
        }
    }

    /// <summary>
    /// Changes the newest unfinished step with <paramref name="callId"/>. Some providers reuse call ids such as
    /// "call_0" across responses, so finished steps with the same id are left alone.
    /// </summary>
    private void UpdateStep(string runId, string callId, Func<SubagentStep, SubagentStep> change) =>
        _subagents.Update(runId, run =>
        {
            var steps = run.StepList.ToList();
            var index = steps.FindLastIndex(step => step.CallId == callId
                && step.Status is SubagentStepStatus.Running or SubagentStepStatus.AwaitingApproval);
            if (index < 0)
            {
                return run;
            }

            steps[index] = change(steps[index]);
            return run with { Steps = steps };
        });

    private static string BuildSubagentSection(SubagentDefinition definition) =>
        $"""
        ## You are a subagent
        Kam's main agent started you as a {definition.Name} subagent for one job, described in the first message.
        You cannot talk to the user and nobody answers questions; decide for yourself and finish the job.
        {definition.Instructions.Trim()}
        Your final message is your report: the main agent reads it, the user does not. Make it complete but short; do not paste large outputs.
        """;
}
