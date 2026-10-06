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

namespace SmartVoiceAgent.Infrastructure.Agent.Runtime;

/// <summary>
/// Tool-calling agent loop: the model calls tools natively, each call passes the approval gate,
/// results go back to the model, and the loop repeats until the model answers.
/// </summary>
public sealed class AgentRuntime : IAgentRuntime
{
    private const string DeclinedResult =
        "The user declined this tool call. Do not retry it; continue without it or ask the user how to proceed.";

    private const string NotRunResult = "Not run: the turn was stopped before this call finished.";

    private readonly Func<IChatClient> _chatClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IToolPermissionService _permissions;
    private readonly IAgentSessionStore _sessionStore;
    private readonly AgentRuntimeOptions _options;
    private readonly ILogger<AgentRuntime> _logger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ApprovalAnswer>> _pendingApprovals = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();

    /// <summary>
    /// Creates the runtime.
    /// </summary>
    /// <param name="chatClientFactory">Returns the chat model client; resolved lazily so settings can change.</param>
    /// <param name="scopeFactory">Creates a scope per turn to resolve tool providers.</param>
    /// <param name="permissions">Approval mode and rules.</param>
    /// <param name="sessionStore">Thread persistence.</param>
    /// <param name="options">Runtime limits.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="clock">Optional clock for tests.</param>
    public AgentRuntime(
        Func<IChatClient> chatClientFactory,
        IServiceScopeFactory scopeFactory,
        IToolPermissionService permissions,
        IAgentSessionStore sessionStore,
        IOptions<AgentRuntimeOptions> options,
        ILogger<AgentRuntime> logger,
        Func<DateTimeOffset>? clock = null)
    {
        _chatClientFactory = chatClientFactory;
        _scopeFactory = scopeFactory;
        _permissions = permissions;
        _sessionStore = sessionStore;
        _options = options.Value;
        _logger = logger;
        _clock = clock ?? (() => DateTimeOffset.Now);
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

            using var scope = _scopeFactory.CreateScope();
            var tools = await CollectToolsAsync(scope.ServiceProvider, cancellationToken);
            var toolsByName = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);

            history = (await _sessionStore.LoadAsync(sessionId, cancellationToken)).ToList();
            CloseDanglingToolCalls(history);

            var chatClient = _chatClientFactory();
            var systemPrompt = AgentSystemPrompt.Build(_clock(), tools.Count);
            var toolTokens = AgentContextWindow.EstimateTokens(tools);
            var usage = new TurnUsage();

            if (IsCompactCommand(userMessage))
            {
                var cut = AgentContextWindow.FindCompactionCut(history, keepUserTurns: 0);
                var compacted = cut is not null
                    && await CompactAsync(sessionId, history, cut.Value, systemPrompt, toolTokens, chatClient, usage, events, cancellationToken);
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

            for (var iteration = 0; iteration < Math.Max(1, _options.MaxIterations); iteration++)
            {
                var modelMessages = await PrepareModelMessagesAsync(
                    sessionId, history, systemPrompt, toolTokens, chatClient, usage, events, cancellationToken);

                var updates = new List<ChatResponseUpdate>();
                await foreach (var update in chatClient.GetStreamingResponseAsync(modelMessages, chatOptions, cancellationToken))
                {
                    updates.Add(update);
                    var text = update.Text;
                    if (!string.IsNullOrEmpty(text))
                    {
                        await events.WriteAsync(new AgentTextDelta(sessionId, text), cancellationToken);
                    }
                }

                var response = updates.ToChatResponse();
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
                        new AgentTurnCompleted(sessionId, response.Text, toolCallCount, usage.InputTokens, usage.OutputTokens),
                        CancellationToken.None);
                    return;
                }

                var results = new List<AIContent>(calls.Count);
                foreach (var call in calls)
                {
                    toolCallCount++;
                    results.Add(await RunToolCallAsync(sessionId, call, toolsByName, events, cancellationToken));
                }

                history.Add(new ChatMessage(ChatRole.Tool, results));
                await _sessionStore.SaveAsync(sessionId, history, CancellationToken.None);
            }

            await events.WriteAsync(
                new AgentTurnFailed(
                    sessionId,
                    $"Stopped after {_options.MaxIterations} steps without a final answer. Ask me to continue if you want me to keep going."),
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
        CancellationToken cancellationToken)
    {
        var budget = _options.ContextTokenBudget;
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
            && await CompactAsync(sessionId, history, cut.Value, systemPrompt, toolTokens, chatClient, usage, events, cancellationToken))
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
        CancellationToken cancellationToken)
    {
        var start = AgentContextWindow.FindActiveStart(history);
        var tokensBefore = toolTokens + AgentContextWindow.EstimateTokens(
            AgentContextWindow.BuildModelMessages(systemPrompt, history, shortenOldToolResults: false));
        var maxTranscriptCharacters = Math.Max(8000, _options.ContextTokenBudget * 3);

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

    private async Task<AIContent> RunToolCallAsync(
        string sessionId,
        FunctionCallContent call,
        IReadOnlyDictionary<string, AgentToolDescriptor> toolsByName,
        ChannelWriter<AgentEvent> events,
        CancellationToken cancellationToken)
    {
        var argumentsJson = SerializeArguments(call.Arguments);

        if (!toolsByName.TryGetValue(call.Name, out var tool))
        {
            var unknown = $"Error: there is no tool named '{call.Name}'.";
            await events.WriteAsync(
                new AgentToolCallCompleted(sessionId, call.CallId, call.Name, false, unknown),
                cancellationToken);
            return new FunctionResultContent(call.CallId, unknown);
        }

        await events.WriteAsync(
            new AgentToolCallStarted(sessionId, call.CallId, tool.Name, tool.DisplayName, argumentsJson, tool.Risk),
            cancellationToken);

        var decision = _permissions.Evaluate(tool, argumentsJson);
        if (decision == ToolPermissionDecision.Ask)
        {
            var answer = await WaitForApprovalAsync(sessionId, call, tool, argumentsJson, events, cancellationToken);
            if (answer.Approved && answer.AlwaysAllow)
            {
                _permissions.AlwaysAllow(tool.Name);
            }

            decision = answer.Approved ? ToolPermissionDecision.Allow : ToolPermissionDecision.Deny;
        }

        if (decision == ToolPermissionDecision.Deny)
        {
            await events.WriteAsync(
                new AgentToolCallCompleted(sessionId, call.CallId, tool.Name, false, "Declined"),
                cancellationToken);
            return new FunctionResultContent(call.CallId, DeclinedResult);
        }

        string resultText;
        bool success;
        try
        {
            var result = await tool.Function.InvokeAsync(
                new AIFunctionArguments(call.Arguments ?? new Dictionary<string, object?>()),
                cancellationToken);
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

        resultText = Truncate(resultText, _options.MaxToolResultCharacters);
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
                    tool.Risk),
                cancellationToken);
            return await pending.Task;
        }
        finally
        {
            _pendingApprovals.TryRemove(requestId, out _);
        }
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

    private readonly record struct ApprovalAnswer(bool Approved, bool AlwaysAllow);

    private sealed class TurnUsage
    {
        public long InputTokens { get; private set; }

        public long OutputTokens { get; private set; }

        public void Add(UsageDetails? usage)
        {
            InputTokens += usage?.InputTokenCount ?? 0;
            OutputTokens += usage?.OutputTokenCount ?? 0;
        }
    }
}
