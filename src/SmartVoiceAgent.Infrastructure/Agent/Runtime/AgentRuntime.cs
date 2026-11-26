using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Infrastructure.Agent.Subagents;

namespace SmartVoiceAgent.Infrastructure.Agent.Runtime;

/// <summary>
/// Tool-calling agent loop: the model calls tools natively, each call passes the approval gate,
/// results go back to the model, and the loop repeats until the model answers. A model request that
/// fails with a temporary error is sent again a few times, and read-only calls from one response run together.
/// </summary>
public sealed partial class AgentRuntime : IAgentRuntime
{
    private const string DeclinedResult =
        "The user declined this tool call. Do not retry it; continue without it or ask the user how to proceed.";

    private const string BlockedResult =
        "Blocked: the user's permission rules deny this call. Do not retry it; continue without it or tell the user.";

    private const string NotRunResult = "Not run: the turn was stopped before this call finished.";

    private static readonly TimeSpan MaxModelRetryDelay = TimeSpan.FromSeconds(30);

    private readonly Func<string?, IChatClient> _chatClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IToolPermissionService _permissions;
    private readonly IAgentSessionStore _sessionStore;
    private readonly IOptionsMonitor<AgentRuntimeOptions> _options;
    private readonly ILogger<AgentRuntime> _logger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string?> _workspaceRoot;
    private readonly ISubagentRunTracker _subagents;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ApprovalAnswer>> _pendingApprovals = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();

    /// <summary>
    /// Creates a runtime where every thread uses the same model.
    /// </summary>
    /// <param name="chatClientFactory">Returns the chat model client; resolved lazily so settings can change.</param>
    /// <param name="scopeFactory">Creates a scope per turn to resolve tool providers.</param>
    /// <param name="permissions">Approval mode and rules.</param>
    /// <param name="sessionStore">Thread persistence.</param>
    /// <param name="options">Runtime limits; the current values are read at the start of each turn.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="clock">Optional clock for tests.</param>
    /// <param name="workspaceRoot">Returns the folder the user is working in, or null when none is selected.</param>
    /// <param name="subagents">Follows the subagents turns start; null keeps them in memory only.</param>
    public AgentRuntime(
        Func<IChatClient> chatClientFactory,
        IServiceScopeFactory scopeFactory,
        IToolPermissionService permissions,
        IAgentSessionStore sessionStore,
        IOptionsMonitor<AgentRuntimeOptions> options,
        ILogger<AgentRuntime> logger,
        Func<DateTimeOffset>? clock = null,
        Func<string?>? workspaceRoot = null,
        ISubagentRunTracker? subagents = null)
        : this(_ => chatClientFactory(), scopeFactory, permissions, sessionStore, options, logger, clock, workspaceRoot, subagents)
    {
    }

    /// <summary>
    /// Creates the runtime.
    /// </summary>
    /// <param name="chatClientFactory">
    /// Returns the chat model client for a model id, or for the model chosen in Settings when the id is null.
    /// Called at the start of each turn, so settings can change between turns.
    /// </param>
    /// <param name="scopeFactory">Creates a scope per turn to resolve tool providers.</param>
    /// <param name="permissions">Approval mode and rules.</param>
    /// <param name="sessionStore">Thread persistence.</param>
    /// <param name="options">Runtime limits; the current values are read at the start of each turn.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="clock">Optional clock for tests.</param>
    /// <param name="workspaceRoot">Returns the folder the user is working in, or null when none is selected.</param>
    /// <param name="subagents">Follows the subagents turns start; null keeps them in memory only.</param>
    public AgentRuntime(
        Func<string?, IChatClient> chatClientFactory,
        IServiceScopeFactory scopeFactory,
        IToolPermissionService permissions,
        IAgentSessionStore sessionStore,
        IOptionsMonitor<AgentRuntimeOptions> options,
        ILogger<AgentRuntime> logger,
        Func<DateTimeOffset>? clock = null,
        Func<string?>? workspaceRoot = null,
        ISubagentRunTracker? subagents = null)
    {
        _chatClientFactory = chatClientFactory;
        _scopeFactory = scopeFactory;
        _permissions = permissions;
        _sessionStore = sessionStore;
        _options = options;
        _logger = logger;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _workspaceRoot = workspaceRoot ?? (() => null);
        _subagents = subagents ?? new SubagentRunTracker(path: null);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentEvent> RunTurnAsync(
        string sessionId,
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<AgentEvent>(new UnboundedChannelOptions { SingleReader = true });

        // Stopping enumeration early (the UI closes or the user starts over) also stops the turn.
        using var turnCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var turn = Task.Run(
            () => RunTurnCoreAsync(sessionId, userMessage, channel.Writer, turnCancellation.Token),
            CancellationToken.None);

        try
        {
            await foreach (var agentEvent in channel.Reader.ReadAllAsync(CancellationToken.None))
            {
                yield return agentEvent;
            }
        }
        finally
        {
            if (!turn.IsCompleted)
            {
                turnCancellation.Cancel();
            }

            await turn;
        }
    }

    /// <inheritdoc />
    public bool ResolveApproval(string requestId, bool approved, bool alwaysAllow = false)
    {
        return _pendingApprovals.TryRemove(requestId, out var pending)
            && pending.TrySetResult(new ApprovalAnswer(approved, alwaysAllow));
    }

    private async Task RunTurnCoreAsync(
        string sessionId,
        string userMessage,
        ChannelWriter<AgentEvent> events,
        CancellationToken cancellationToken)
    {
        var sessionLock = _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        List<ChatMessage>? history = null;
        var lockTaken = false;

        try
        {
            await sessionLock.WaitAsync(cancellationToken);
            lockTaken = true;
            var limits = TurnLimits.From(_options.CurrentValue);

            using var scope = _scopeFactory.CreateScope();
            var tools = await CollectToolsAsync(scope.ServiceProvider, cancellationToken);

            history = (await _sessionStore.LoadAsync(sessionId, cancellationToken)).ToList();
            CloseDanglingToolCalls(history);

            // A thread can pick its own model; otherwise the model chosen in Settings runs it.
            var session = await _sessionStore.GetSummaryAsync(sessionId, cancellationToken);
            var chatClient = _chatClientFactory(session?.ModelId);
            var promptSections = CollectPromptSections(scope.ServiceProvider);
            // The built-in task tool wins over a provider tool of the same name.
            tools.RemoveAll(tool => tool.Name == SubagentTaskFunction.ToolName);
            tools.Add(CreateSubagentTool(new SubagentTurn(
                sessionId, session?.ModelId, chatClient, tools.ToList(), promptSections, events, limits)));
            var toolsByName = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
            var systemPrompt = AgentSystemPrompt.Build(
                _clock(),
                tools.Count,
                _workspaceRoot(),
                promptSections);
            var toolTokens = AgentContextWindow.EstimateTokens(tools);
            var usage = new TurnUsage();

            if (IsCompactCommand(userMessage))
            {
                var cut = AgentContextWindow.FindCompactionCut(history, keepUserTurns: 0);
                var compacted = cut is not null
                    && await CompactAsync(sessionId, history, cut.Value, systemPrompt, toolTokens, chatClient, usage, events, limits, cancellationToken);
                await events.WriteAsync(
                    new AgentTurnCompleted(
                        sessionId,
                        compacted ? "Compacted the conversation. I'll continue from the summary." : "There is nothing to compact yet.",
                        0,
                        usage.InputTokens,
                        usage.OutputTokens),
                    CancellationToken.None);
                return;
            }

            history.Add(new ChatMessage(ChatRole.User, userMessage));

            var chatOptions = new ChatOptions
            {
                Tools = tools.Select(tool => (AITool)tool.Function).ToList(),
                ToolMode = tools.Count > 0 ? ChatToolMode.Auto : ChatToolMode.None,
                AllowMultipleToolCalls = true
            };

            var toolCallCount = 0;

            for (var iteration = 0; iteration < limits.MaxIterations; iteration++)
            {
                var modelMessages = await PrepareModelMessagesAsync(
                    sessionId, history, systemPrompt, toolTokens, chatClient, usage, events, limits, cancellationToken);

                var response = await GetModelResponseAsync(
                    sessionId, chatClient, modelMessages, chatOptions, events, cancellationToken);
                usage.Add(response.Usage);
                history.AddRange(response.Messages);

                var calls = response.Messages
                    .SelectMany(message => message.Contents)
                    .OfType<FunctionCallContent>()
                    .ToList();

                if (calls.Count == 0)
                {
                    await _sessionStore.SaveAsync(sessionId, history, CancellationToken.None);
                    await events.WriteAsync(
                        new AgentTurnCompleted(sessionId, response.Text, toolCallCount, usage.InputTokens, usage.OutputTokens, usage.LastInputTokens),
                        CancellationToken.None);
                    return;
                }

                toolCallCount += calls.Count;
                var results = await RunToolCallsAsync(sessionId, calls, toolsByName, events, limits, cancellationToken);

                history.Add(new ChatMessage(ChatRole.Tool, [.. results]));
                await _sessionStore.SaveAsync(sessionId, history, CancellationToken.None);
            }

            await events.WriteAsync(
                new AgentTurnFailed(
                    sessionId,
                    $"Stopped after {limits.MaxIterations} steps without a final answer. Ask me to continue if you want me to keep going."),
                CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await events.WriteAsync(new AgentTurnFailed(sessionId, "Stopped."), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent turn failed for session {SessionId}", sessionId);
            await events.WriteAsync(new AgentTurnFailed(sessionId, ex.Message), CancellationToken.None);
        }
        finally
        {
            if (history is not null)
            {
                CloseDanglingToolCalls(history);
                await TrySaveAsync(sessionId, history);
            }

            if (lockTaken)
            {
                sessionLock.Release();
            }

            events.TryComplete();
        }
    }

    /// <summary>
    /// Streams one model response. A temporary failure (timeout, 429, 5xx, dropped stream) sends the same
    /// request again after a growing wait, at most <see cref="AgentRuntimeOptions.MaxModelRetries"/> times;
    /// any other failure ends the turn at once. Nothing from a failed attempt reaches the thread, and a
    /// Stop during the wait cancels the retry.
    /// </summary>
    private async Task<ChatResponse> GetModelResponseAsync(
        string sessionId,
        IChatClient chatClient,
        List<ChatMessage> modelMessages,
        ChatOptions chatOptions,
        ChannelWriter<AgentEvent> events,
        CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(0, _options.CurrentValue.MaxModelRetries);

        for (var retry = 0; ; retry++)
        {
            var streamedText = false;
            try
            {
                var updates = new List<ChatResponseUpdate>();
                await foreach (var update in chatClient.GetStreamingResponseAsync(modelMessages, chatOptions, cancellationToken))
                {
                    updates.Add(update);
                    var text = update.Text;
                    if (!string.IsNullOrEmpty(text))
                    {
                        streamedText = true;
                        await events.WriteAsync(new AgentTextDelta(sessionId, text), cancellationToken);
                    }
                }

                return updates.ToChatResponse();
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                var error = ModelErrorClassifier.Classify(ex);
                if (!error.IsTransient)
                {
                    throw new ModelRequestException(error.UserMessage, ex);
                }

                if (retry >= maxRetries)
                {
                    throw new ModelRequestException(
                        maxRetries == 0
                            ? $"The model request failed: {error.Reason}. Try again in a moment."
                            : $"The model did not answer after {maxRetries + 1} attempts. Last error: {error.Reason}. Check your connection or try again in a moment.",
                        ex);
                }

                var delay = ModelRetryDelay(retry + 1, _options.CurrentValue.ModelRetryBaseDelayMilliseconds);
                _logger.LogWarning(
                    ex,
                    "Model request failed in session {SessionId} ({Reason}); retry {Retry} of {MaxRetries} in {Delay} ms",
                    sessionId,
                    error.Reason,
                    retry + 1,
                    maxRetries,
                    delay.TotalMilliseconds);
                await events.WriteAsync(
                    new AgentModelRetryScheduled(sessionId, retry + 1, maxRetries, delay, error.Reason, streamedText),
                    cancellationToken);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Returns the wait before a retry: the base delay, doubled for each retry after the first, at most 30 seconds.
    /// </summary>
    /// <param name="retry">The retry about to be made, counted from 1.</param>
    /// <param name="baseDelayMilliseconds">The wait before the first retry.</param>
    public static TimeSpan ModelRetryDelay(int retry, int baseDelayMilliseconds)
    {
        var milliseconds = Math.Max(0, baseDelayMilliseconds) * Math.Pow(2, Math.Max(0, retry - 1));
        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, MaxModelRetryDelay.TotalMilliseconds));
    }

    /// <summary>
    /// Fits the request into the context budget: first by shortening older tool results in the request only,
    /// then by summarizing earlier turns into the thread.
    /// </summary>
    private async Task<List<ChatMessage>> PrepareModelMessagesAsync(
        string sessionId,
        List<ChatMessage> history,
        string systemPrompt,
        int toolTokens,
        IChatClient chatClient,
        TurnUsage usage,
        ChannelWriter<AgentEvent> events,
        TurnLimits limits,
        CancellationToken cancellationToken)
    {
        var budget = limits.ContextTokenBudget;
        var messages = AgentContextWindow.BuildModelMessages(systemPrompt, history, shortenOldToolResults: false);
        if (budget <= 0 || toolTokens + AgentContextWindow.EstimateTokens(messages) <= budget)
        {
            return messages;
        }

        messages = AgentContextWindow.BuildModelMessages(systemPrompt, history, shortenOldToolResults: true);
        if (toolTokens + AgentContextWindow.EstimateTokens(messages) <= budget)
        {
            return messages;
        }

        // Keep the last two user turns when they fit in half the budget, otherwise only the current one.
        var cut = AgentContextWindow.FindCompactionCut(history, keepUserTurns: 2);
        if (cut is null || EstimateKeptTokens(history, cut.Value, systemPrompt) > budget / 2)
        {
            cut = AgentContextWindow.FindCompactionCut(history, keepUserTurns: 1) ?? cut;
        }

        if (cut is not null
            && await CompactAsync(sessionId, history, cut.Value, systemPrompt, toolTokens, chatClient, usage, events, limits, cancellationToken))
        {
            messages = AgentContextWindow.BuildModelMessages(systemPrompt, history, shortenOldToolResults: true);
        }

        return messages;
    }

    private async Task<bool> CompactAsync(
        string sessionId,
        List<ChatMessage> history,
        int cut,
        string systemPrompt,
        int toolTokens,
        IChatClient chatClient,
        TurnUsage usage,
        ChannelWriter<AgentEvent> events,
        TurnLimits limits,
        CancellationToken cancellationToken)
    {
        var start = AgentContextWindow.FindActiveStart(history);
        var tokensBefore = toolTokens + AgentContextWindow.EstimateTokens(
            AgentContextWindow.BuildModelMessages(systemPrompt, history, shortenOldToolResults: false));
        var maxTranscriptCharacters = Math.Max(8000, limits.ContextTokenBudget * 3);

        string summary;
        try
        {
            var response = await chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, AgentContextWindow.SummaryInstructions),
                    new ChatMessage(
                        ChatRole.User,
                        "Summarize this conversation:\n\n" + AgentContextWindow.RenderTranscript(history, cut, maxTranscriptCharacters))
                ],
                new ChatOptions { MaxOutputTokens = 2000 },
                cancellationToken);
            usage.Add(response.Usage);
            summary = response.Text;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not compact agent session {SessionId}", sessionId);
            return false;
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            return false;
        }

        history.Insert(cut, AgentContextWindow.CreateSummaryMessage(summary));
        await _sessionStore.SaveAsync(sessionId, history, CancellationToken.None);

        var tokensAfter = toolTokens + AgentContextWindow.EstimateTokens(
            AgentContextWindow.BuildModelMessages(systemPrompt, history, shortenOldToolResults: false));
        _logger.LogInformation(
            "Compacted {Count} messages in agent session {SessionId}: ~{Before} to ~{After} tokens",
            cut - start,
            sessionId,
            tokensBefore,
            tokensAfter);
        await events.WriteAsync(
            new AgentContextCompacted(sessionId, cut - start, tokensBefore, tokensAfter),
            cancellationToken);
        return true;
    }

    private static int EstimateKeptTokens(IReadOnlyList<ChatMessage> history, int cut, string systemPrompt)
    {
        var kept = history.Skip(cut).ToList();
        return AgentContextWindow.EstimateTokens(
            AgentContextWindow.BuildModelMessages(systemPrompt, kept, shortenOldToolResults: true));
    }

    private static bool IsCompactCommand(string message) =>
        message.Trim().Equals("/compact", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs the tool calls of one model response and returns their results in the order the model asked.
    /// The permission gate handles one call at a time, in that order, so approvals never overlap. Calls that
    /// only read run together; a call that changes something waits for the reads before it and runs alone.
    /// </summary>
    private async Task<AIContent[]> RunToolCallsAsync(
        string sessionId,
        IReadOnlyList<FunctionCallContent> calls,
        IReadOnlyDictionary<string, AgentToolDescriptor> toolsByName,
        ChannelWriter<AgentEvent> events,
        TurnLimits limits,
        CancellationToken cancellationToken)
    {
        var results = new AIContent[calls.Count];
        var maxParallel = Math.Max(1, _options.CurrentValue.MaxParallelToolCalls);
        var reads = new List<Task>();
        using var batch = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var slots = new SemaphoreSlim(maxParallel, maxParallel);

        try
        {
            for (var index = 0; index < calls.Count; index++)
            {
                var call = calls[index];
                var (tool, refusal) = await GateToolCallAsync(sessionId, call, toolsByName, events, batch.Token);
                if (tool is null)
                {
                    results[index] = refusal!;
                    continue;
                }

                if (maxParallel > 1 && CanRunTogether(tool, call))
                {
                    var slot = index;
                    reads.Add(Task.Run(
                        async () =>
                        {
                            await slots.WaitAsync(batch.Token);
                            try
                            {
                                results[slot] = await InvokeToolAsync(sessionId, call, tool, events, limits.MaxToolResultCharacters, batch.Token);
                            }
                            finally
                            {
                                slots.Release();
                            }
                        },
                        CancellationToken.None));
                    continue;
                }

                await Task.WhenAll(reads);
                reads.Clear();
                results[index] = await InvokeToolAsync(sessionId, call, tool, events, limits.MaxToolResultCharacters, batch.Token);
            }

            await Task.WhenAll(reads);
            return results;
        }
        catch
        {
            // The turn is ending: stop the reads still running and wait for them, so no tool outlives it.
            await batch.CancelAsync();
            await Task.WhenAll(reads).ContinueWith(
                _ => { },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
    }

    /// <summary>
    /// Read-only tools (local reads and network reads without side effects) and read-only subagents may run at
    /// the same time. Everything that writes, executes, acts outside the machine or delegates runs alone, and so
    /// does a subagent that may do those things.
    /// </summary>
    private static bool CanRunTogether(AgentToolDescriptor tool, FunctionCallContent call) =>
        tool.Risk is ToolRisk.Read or ToolRisk.Network
        || (tool.Function is SubagentTaskFunction && SubagentTaskFunction.StartsReadOnlySubagent(call.Arguments));

    /// <summary>
    /// Announces a call and passes it through the permission policy, asking the user when needed.
    /// Returns the tool to run, or the result to give the model when the call must not run.
    /// </summary>
    private async Task<(AgentToolDescriptor? Tool, AIContent? Refusal)> GateToolCallAsync(
        string sessionId,
        FunctionCallContent call,
        IReadOnlyDictionary<string, AgentToolDescriptor> toolsByName,
        ChannelWriter<AgentEvent> events,
        CancellationToken cancellationToken)
    {
        if (!toolsByName.TryGetValue(call.Name, out var tool))
        {
            var unknown = $"Error: there is no tool named '{call.Name}'.";
            await events.WriteAsync(
                new AgentToolCallCompleted(sessionId, call.CallId, call.Name, false, unknown),
                cancellationToken);
            return (null, new FunctionResultContent(call.CallId, unknown));
        }

        if (tool.PrepareArguments is not null)
        {
            call.Arguments = tool.PrepareArguments(call.Arguments ?? new Dictionary<string, object?>());
        }

        var argumentsJson = SerializeArguments(call.Arguments);
        await events.WriteAsync(
            new AgentToolCallStarted(sessionId, call.CallId, tool.Name, tool.DisplayName, argumentsJson, tool.Risk),
            cancellationToken);

        var decision = _permissions.Evaluate(tool, argumentsJson);
        if (decision == ToolPermissionDecision.Deny)
        {
            await events.WriteAsync(
                new AgentToolCallCompleted(sessionId, call.CallId, tool.Name, false, "Blocked by a deny rule"),
                cancellationToken);
            return (null, new FunctionResultContent(call.CallId, BlockedResult));
        }

        if (decision == ToolPermissionDecision.Ask)
        {
            var alwaysAllowRule = _permissions.SuggestAllowRule(tool, argumentsJson);
            var answer = await WaitForApprovalAsync(sessionId, call, tool, argumentsJson, alwaysAllowRule, events, cancellationToken);
            await events.WriteAsync(new AgentApprovalResolved(sessionId, answer.RequestId, call.CallId, answer.Approved), cancellationToken);
            if (answer.Approved && answer.AlwaysAllow && !string.IsNullOrWhiteSpace(alwaysAllowRule))
            {
                _permissions.AddRule(alwaysAllowRule, allow: true);
            }

            decision = answer.Approved ? ToolPermissionDecision.Allow : ToolPermissionDecision.Deny;
        }

        if (decision == ToolPermissionDecision.Deny)
        {
            await events.WriteAsync(
                new AgentToolCallCompleted(sessionId, call.CallId, tool.Name, false, "Declined"),
                cancellationToken);
            return (null, new FunctionResultContent(call.CallId, DeclinedResult));
        }

        return (tool, null);
    }

    private async Task<AIContent> InvokeToolAsync(
        string sessionId,
        FunctionCallContent call,
        AgentToolDescriptor tool,
        ChannelWriter<AgentEvent> events,
        int maxResultCharacters,
        CancellationToken cancellationToken)
    {
        string resultText;
        bool success;
        try
        {
            var arguments = new AIFunctionArguments(call.Arguments ?? new Dictionary<string, object?>());
            var result = tool.Function is SubagentTaskFunction subagent
                ? await subagent.RunAsync(call.CallId, arguments, cancellationToken)
                : await tool.Function.InvokeAsync(arguments, cancellationToken);
            resultText = StringifyResult(result);
            success = !resultText.StartsWith("Error", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool {Tool} failed", tool.Name);
            resultText = $"Error: {ex.Message}";
            success = false;
        }

        resultText = Truncate(resultText, maxResultCharacters);
        await events.WriteAsync(
            new AgentToolCallCompleted(sessionId, call.CallId, tool.Name, success, Summarize(resultText)),
            cancellationToken);
        return new FunctionResultContent(call.CallId, resultText);
    }

    private async Task<ApprovalAnswer> WaitForApprovalAsync(
        string sessionId,
        FunctionCallContent call,
        AgentToolDescriptor tool,
        string argumentsJson,
        string alwaysAllowRule,
        ChannelWriter<AgentEvent> events,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var pending = new TaskCompletionSource<ApprovalAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingApprovals[requestId] = pending;

        try
        {
            await using var registration = cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
            await events.WriteAsync(
                new AgentApprovalRequested(
                    sessionId,
                    requestId,
                    call.CallId,
                    tool.Name,
                    tool.DisplayName,
                    argumentsJson,
                    tool.Risk,
                    alwaysAllowRule),
                cancellationToken);
            return (await pending.Task) with { RequestId = requestId };
        }
        finally
        {
            _pendingApprovals.TryRemove(requestId, out _);
        }
    }

    private List<string> CollectPromptSections(IServiceProvider services)
    {
        var sections = new List<string>();
        foreach (var contributor in services.GetServices<IAgentPromptContributor>())
        {
            try
            {
                if (contributor.GetPromptSection() is { Length: > 0 } section)
                {
                    sections.Add(section);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Prompt section from {Contributor} failed", contributor.GetType().Name);
            }
        }

        return sections;
    }

    private static async Task<List<AgentToolDescriptor>> CollectToolsAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var tools = new List<AgentToolDescriptor>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in services.GetServices<IAgentToolProvider>())
        {
            foreach (var tool in await provider.GetToolsAsync(cancellationToken))
            {
                // The first provider to offer a name wins, so built-ins cannot be shadowed by plugins.
                if (names.Add(tool.Name))
                {
                    tools.Add(tool);
                }
            }
        }

        return tools;
    }

    /// <summary>
    /// Gives every tool call without a result a placeholder result. Providers reject a history where an
    /// assistant tool call is not followed by its result, which happens when a turn is stopped mid-call.
    /// </summary>
    /// <param name="history">The conversation, changed in place.</param>
    public static void CloseDanglingToolCalls(List<ChatMessage> history)
    {
        for (var index = 0; index < history.Count; index++)
        {
            if (history[index].Role != ChatRole.Assistant)
            {
                continue;
            }

            var calls = history[index].Contents.OfType<FunctionCallContent>().ToList();
            if (calls.Count == 0)
            {
                continue;
            }

            var answered = new HashSet<string>(StringComparer.Ordinal);
            var next = index + 1;
            while (next < history.Count && history[next].Role == ChatRole.Tool)
            {
                foreach (var result in history[next].Contents.OfType<FunctionResultContent>())
                {
                    answered.Add(result.CallId);
                }

                next++;
            }

            var missing = calls
                .Where(call => !answered.Contains(call.CallId))
                .Select(call => (AIContent)new FunctionResultContent(call.CallId, NotRunResult))
                .ToList();
            if (missing.Count > 0)
            {
                history.Insert(next, new ChatMessage(ChatRole.Tool, missing));
            }
        }
    }

    private async Task TrySaveAsync(string sessionId, IReadOnlyList<ChatMessage> history)
    {
        try
        {
            await _sessionStore.SaveAsync(sessionId, history, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save agent session {SessionId}", sessionId);
        }
    }

    private static string SerializeArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return "{}";
        }

        try
        {
            return JsonSerializer.Serialize(arguments, AIJsonUtilities.DefaultOptions);
        }
        catch (NotSupportedException)
        {
            return "{}";
        }
    }

    private static string StringifyResult(object? result)
    {
        return result switch
        {
            null => "Done.",
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
            JsonElement element => element.GetRawText(),
            _ => JsonSerializer.Serialize(result, AIJsonUtilities.DefaultOptions)
        };
    }

    private static string Truncate(string text, int maxCharacters)
    {
        if (maxCharacters <= 0 || text.Length <= maxCharacters)
        {
            return text;
        }

        return text[..maxCharacters] + $"\n[truncated {text.Length - maxCharacters} characters]";
    }

    private static string Summarize(string text)
    {
        var firstLine = text.ReplaceLineEndings("\n").Split('\n', 2)[0].Trim();
        return firstLine.Length <= 160 ? firstLine : firstLine[..160] + "...";
    }

    private readonly record struct ApprovalAnswer(bool Approved, bool AlwaysAllow, string RequestId = "");

    /// <summary>The limits one turn runs with, read once when the turn starts and clamped to safe ranges.</summary>
    private readonly record struct TurnLimits(int MaxIterations, int MaxToolResultCharacters, int ContextTokenBudget)
    {
        public static TurnLimits From(AgentRuntimeOptions options) => new(
            AgentRuntimeOptions.ClampMaxIterations(options.MaxIterations),
            AgentRuntimeOptions.ClampMaxToolResultCharacters(options.MaxToolResultCharacters),
            AgentRuntimeOptions.ClampContextTokenBudget(options.ContextTokenBudget));
    }

    /// <summary>A model request failed for good; the message is written for the user.</summary>
    private sealed class ModelRequestException(string message, Exception innerException)
        : Exception(message, innerException);

    private sealed class TurnUsage
    {
        public long InputTokens { get; private set; }

        public long OutputTokens { get; private set; }

        /// <summary>Gets the input size of the most recent model request that reported usage.</summary>
        public long LastInputTokens { get; private set; }

        public void Add(UsageDetails? usage)
        {
            InputTokens += usage?.InputTokenCount ?? 0;
            OutputTokens += usage?.OutputTokenCount ?? 0;
            if (usage?.InputTokenCount is > 0 and var last)
            {
                LastInputTokens = last;
            }
        }
    }
}
