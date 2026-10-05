using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Infrastructure.Agent.Runtime;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Runtime;

public sealed class AgentRuntimeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kam-agent-runtime-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunTurnAsync_ToolCallThenAnswer_RunsToolAndReturnsResultToModel()
    {
        var readCalls = new List<string>();
        var tool = Tool(
            AIFunctionFactory.Create((string path) => { readCalls.Add(path); return "hello from " + path; }, "files_read"),
            ToolRisk.Read);
        var chat = new ScriptedChatClient(
            [Call("call-1", "files_read", ("path", "notes.txt"))],
            [new TextContent("The file says "), new TextContent("hello."), new UsageContent(new UsageDetails { InputTokenCount = 12, OutputTokenCount = 4 })]);
        var runtime = CreateRuntime(chat, [tool]);

        var events = await CollectAsync(runtime, "s1", "read notes.txt");

        readCalls.Should().Equal("notes.txt");
        events.OfType<AgentToolCallStarted>().Should().ContainSingle(e => e.ToolName == "files_read" && e.Risk == ToolRisk.Read);
        events.OfType<AgentToolCallCompleted>().Should().ContainSingle(e => e.Success && e.Summary == "hello from notes.txt");
        string.Concat(events.OfType<AgentTextDelta>().Select(e => e.Text)).Should().Be("The file says hello.");
        events.OfType<AgentApprovalRequested>().Should().BeEmpty();
        var completed = events.Last().Should().BeOfType<AgentTurnCompleted>().Subject;
        completed.FinalText.Should().Be("The file says hello.");
        completed.ToolCallCount.Should().Be(1);
        completed.InputTokens.Should().Be(12);

        chat.Requests.Should().HaveCount(2);
        chat.Requests[0][0].Role.Should().Be(ChatRole.System);
        chat.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .Should().ContainSingle(r => r.CallId == "call-1" && r.Result!.ToString() == "hello from notes.txt");
    }

    [Fact]
    public async Task RunTurnAsync_RiskyToolInAskMode_WaitsForApprovalBeforeRunning()
    {
        var written = 0;
        var tool = Tool(AIFunctionFactory.Create((string path) => { written++; return "saved"; }, "files_write"), ToolRisk.Write);
        var chat = new ScriptedChatClient(
            [Call("call-1", "files_write", ("path", "a.txt"))],
            [new TextContent("Saved.")]);
        var runtime = CreateRuntime(chat, [tool]);

        var events = new List<AgentEvent>();
        await foreach (var agentEvent in runtime.RunTurnAsync("s1", "save it", TestContext.Current.CancellationToken))
        {
            events.Add(agentEvent);
            if (agentEvent is AgentApprovalRequested request)
            {
                written.Should().Be(0, "the tool must not run before the user answers");
                request.ArgumentsJson.Should().Contain("a.txt");
                runtime.ResolveApproval(request.RequestId, approved: true).Should().BeTrue();
            }
        }

        written.Should().Be(1);
        events.OfType<AgentApprovalRequested>().Should().ContainSingle();
        events.Last().Should().BeOfType<AgentTurnCompleted>();
    }

    [Fact]
    public async Task RunTurnAsync_DeclinedCall_DoesNotRunToolAndTellsModel()
    {
        var ran = false;
        var tool = Tool(AIFunctionFactory.Create(() => { ran = true; return "killed"; }, "processes_kill"), ToolRisk.Execute);
        var chat = new ScriptedChatClient(
            [Call("call-1", "processes_kill")],
            [new TextContent("Okay, I left it running.")]);
        var runtime = CreateRuntime(chat, [tool]);

        var events = await CollectAsync(runtime, "s1", "kill it", approve: false);

        ran.Should().BeFalse();
        events.OfType<AgentToolCallCompleted>().Should().ContainSingle(e => !e.Success && e.Summary == "Declined");
        chat.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .Single().Result!.ToString().Should().Contain("declined");
    }

    [Fact]
    public async Task RunTurnAsync_AlwaysAllow_SavesRuleSoNextCallRunsWithoutAsking()
    {
        var tool = Tool(AIFunctionFactory.Create(() => "ok", "shell_run"), ToolRisk.Execute);
        var permissions = new ToolPermissionService(Path.Combine(_directory, "permissions.json"));
        var chat = new ScriptedChatClient(
            [Call("call-1", "shell_run")],
            [new TextContent("first")],
            [Call("call-2", "shell_run")],
            [new TextContent("second")]);
        var runtime = CreateRuntime(chat, [tool], permissions);

        var first = await CollectAsync(runtime, "s1", "run", approve: true, alwaysAllow: true);
        var second = await CollectAsync(runtime, "s1", "run again");

        first.OfType<AgentApprovalRequested>().Should().ContainSingle();
        second.OfType<AgentApprovalRequested>().Should().BeEmpty();
        permissions.AllowedTools.Should().Contain("shell_run");
    }

    [Fact]
    public async Task RunTurnAsync_UnknownTool_ReturnsErrorToModel()
    {
        var chat = new ScriptedChatClient(
            [Call("call-1", "does_not_exist")],
            [new TextContent("Sorry.")]);
        var runtime = CreateRuntime(chat, []);

        var events = await CollectAsync(runtime, "s1", "do it");

        events.OfType<AgentToolCallCompleted>().Should().ContainSingle(e => !e.Success);
        chat.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .Single().Result!.ToString().Should().Contain("no tool named");
        events.Last().Should().BeOfType<AgentTurnCompleted>();
    }

    [Fact]
    public async Task RunTurnAsync_ToolThrows_ReportsFailureAndContinues()
    {
        var tool = Tool(AIFunctionFactory.Create(new Func<string>(() => throw new IOException("disk gone")), "files_read"), ToolRisk.Read);
        var chat = new ScriptedChatClient(
            [Call("call-1", "files_read")],
            [new TextContent("The disk is unavailable.")]);
        var runtime = CreateRuntime(chat, [tool]);

        var events = await CollectAsync(runtime, "s1", "read");

        events.OfType<AgentToolCallCompleted>().Should().ContainSingle(e => !e.Success && e.Summary.Contains("disk gone"));
        events.Last().Should().BeOfType<AgentTurnCompleted>();
    }

    [Fact]
    public async Task RunTurnAsync_ModelNeverAnswers_StopsAtIterationLimit()
    {
        var tool = Tool(AIFunctionFactory.Create(() => "again", "loop_tool"), ToolRisk.Read);
        var chat = new ScriptedChatClient(Enumerable.Range(0, 10).Select(i => (IList<AIContent>)[Call($"c{i}", "loop_tool")]).ToArray());
        var runtime = CreateRuntime(chat, [tool], maxIterations: 3);

        var events = await CollectAsync(runtime, "s1", "loop");

        chat.Requests.Should().HaveCount(3);
        events.Last().Should().BeOfType<AgentTurnFailed>().Which.Message.Should().Contain("3 steps");
    }

    [Fact]
    public async Task RunTurnAsync_LongToolResult_IsTruncatedForModel()
    {
        var tool = Tool(AIFunctionFactory.Create(() => new string('x', 500), "big"), ToolRisk.Read);
        var chat = new ScriptedChatClient([Call("c1", "big")], [new TextContent("done")]);
        var runtime = CreateRuntime(chat, [tool], maxResultCharacters: 100);

        await CollectAsync(runtime, "s1", "go");

        var result = chat.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single().Result!.ToString()!;
        result.Should().StartWith(new string('x', 100)).And.Contain("[truncated 400 characters]");
    }

    [Fact]
    public async Task RunTurnAsync_PersistsHistoryAndSendsItOnNextTurn()
    {
        var store = new JsonAgentSessionStore(Path.Combine(_directory, "sessions"));
        var tool = Tool(AIFunctionFactory.Create(() => "4", "calc"), ToolRisk.Read);
        var chat = new ScriptedChatClient(
            [Call("c1", "calc")],
            [new TextContent("It is 4.")],
            [new TextContent("You asked about 2+2.")]);
        var runtime = CreateRuntime(chat, [tool], store: store);

        await CollectAsync(runtime, "thread-1", "what is 2+2?");
        await CollectAsync(runtime, "thread-1", "what did I ask?");

        var saved = await store.LoadAsync("thread-1", TestContext.Current.CancellationToken);
        saved.Select(m => m.Role).Should().Equal(
            ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant, ChatRole.User, ChatRole.Assistant);
        chat.Requests[2].Where(m => m.Role == ChatRole.User).Select(m => m.Text)
            .Should().Equal("what is 2+2?", "what did I ask?");
        (await store.ListAsync(TestContext.Current.CancellationToken)).Should().ContainSingle(s => s.Id == "thread-1" && s.Title == "what is 2+2?");
    }

    [Fact]
    public async Task RunTurnAsync_ModelThrows_EmitsFailure()
    {
        var chat = new ScriptedChatClient() { Failure = new InvalidOperationException("401 Unauthorized") };
        var runtime = CreateRuntime(chat, []);

        var events = await CollectAsync(runtime, "s1", "hi");

        events.Should().ContainSingle().Which.Should().BeOfType<AgentTurnFailed>().Which.Message.Should().Contain("401");
    }

    [Fact]
    public async Task RunTurnAsync_CancelledWhileWaitingForApproval_StopsTurn()
    {
        var tool = Tool(AIFunctionFactory.Create(() => "ok", "shell_run"), ToolRisk.Execute);
        var chat = new ScriptedChatClient([Call("c1", "shell_run")], [new TextContent("unreachable")]);
        var runtime = CreateRuntime(chat, [tool]);
        using var cancellation = new CancellationTokenSource();

        var events = new List<AgentEvent>();
        await foreach (var agentEvent in runtime.RunTurnAsync("s1", "run", cancellation.Token))
        {
            events.Add(agentEvent);
            if (agentEvent is AgentApprovalRequested)
            {
                cancellation.Cancel();
            }
        }

        events.Last().Should().BeOfType<AgentTurnFailed>().Which.Message.Should().Be("Stopped.");
        chat.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task RunTurnAsync_StoppedMidCall_SavesPlaceholderResultSoNextTurnIsValid()
    {
        var store = new JsonAgentSessionStore(Path.Combine(_directory, "sessions"));
        var tool = Tool(AIFunctionFactory.Create(() => "ok", "shell_run"), ToolRisk.Execute);
        var chat = new ScriptedChatClient(
            [Call("c1", "shell_run"), Call("c2", "shell_run")],
            [new TextContent("Fine.")]);
        var runtime = CreateRuntime(chat, [tool], store: store);
        using var cancellation = new CancellationTokenSource();

        await foreach (var agentEvent in runtime.RunTurnAsync("s1", "run twice", cancellation.Token))
        {
            if (agentEvent is AgentApprovalRequested)
            {
                cancellation.Cancel();
            }
        }

        await CollectAsync(runtime, "s1", "never mind");

        var saved = await store.LoadAsync("s1", TestContext.Current.CancellationToken);
        saved.Select(m => m.Role).Should().Equal(ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.User, ChatRole.Assistant);
        saved[2].Contents.OfType<FunctionResultContent>().Select(r => r.CallId).Should().Equal("c1", "c2");
        chat.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Should().HaveCount(2);
    }

    [Fact]
    public void CloseDanglingToolCalls_InsertsResultsDirectlyAfterTheCall()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, "a"),
            new(ChatRole.Assistant, [new FunctionCallContent("x", "t"), new FunctionCallContent("y", "t")]),
            new(ChatRole.Tool, [new FunctionResultContent("x", "done")]),
            new(ChatRole.User, "b")
        ];

        AgentRuntime.CloseDanglingToolCalls(history);

        history.Select(m => m.Role).Should().Equal(ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Tool, ChatRole.User);
        history[3].Contents.OfType<FunctionResultContent>().Single().CallId.Should().Be("y");
    }

    [Fact]
    public async Task RunTurnAsync_OverContextBudget_SummarizesEarlierTurnsButKeepsThemInThread()
    {
        var store = new JsonAgentSessionStore(Path.Combine(_directory, "sessions"));
        await store.SaveAsync("long", ThreeLongTurns(), TestContext.Current.CancellationToken);
        var chat = new ScriptedChatClient([new TextContent("Still with you.")]) { Summary = "Goals: finish the report." };
        var runtime = CreateRuntime(chat, [], store: store, contextTokenBudget: 6000);

        var events = await CollectAsync(runtime, "long", "fourth");

        var compacted = events.OfType<AgentContextCompacted>().Should().ContainSingle().Subject;
        compacted.SummarizedMessageCount.Should().Be(4);
        compacted.TokensAfter.Should().BeLessThan(compacted.TokensBefore);
        events.Last().Should().BeOfType<AgentTurnCompleted>();

        var summaryInput = chat.SummaryRequests.Should().ContainSingle().Subject.Last().Text;
        summaryInput.Should().Contain("User: first").And.Contain("User: second").And.NotContain("User: third");

        var request = chat.Requests.Should().ContainSingle().Subject;
        request[0].Role.Should().Be(ChatRole.System);
        request[0].Text.Should().Contain(AgentContextWindow.SummaryPrefix).And.Contain("Goals: finish the report.");
        request.Where(m => m.Role == ChatRole.User).Select(m => m.Text).Should().Equal("third", "fourth");

        var saved = await store.LoadAsync("long", TestContext.Current.CancellationToken);
        saved.Select(m => m.Role).Should().Equal(
            ChatRole.User, ChatRole.Assistant, ChatRole.User, ChatRole.Assistant,
            ChatRole.System,
            ChatRole.User, ChatRole.Assistant, ChatRole.User, ChatRole.Assistant);
    }

    [Fact]
    public async Task RunTurnAsync_SlightlyOverBudget_ShortensOldToolResultsWithoutSummarizing()
    {
        var store = new JsonAgentSessionStore(Path.Combine(_directory, "sessions"));
        var callIds = Enumerable.Range(1, 6).Select(i => $"r{i}").ToList();
        await store.SaveAsync(
            "tools",
            [
                new(ChatRole.User, "check everything"),
                new(ChatRole.Assistant, callIds.Select(id => (AIContent)new FunctionCallContent(id, "files_read")).ToList()),
                new(ChatRole.Tool, callIds.Select(id => (AIContent)new FunctionResultContent(id, new string('x', 3000))).ToList()),
                new(ChatRole.Assistant, "All read.")
            ],
            TestContext.Current.CancellationToken);
        var chat = new ScriptedChatClient([new TextContent("Done.")]) { Summary = "unused" };
        var runtime = CreateRuntime(chat, [], store: store, contextTokenBudget: 4200);

        var events = await CollectAsync(runtime, "tools", "again");

        events.OfType<AgentContextCompacted>().Should().BeEmpty();
        chat.SummaryRequests.Should().BeEmpty();
        var results = chat.Requests.Single().SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .ToDictionary(r => r.CallId, r => r.Result!.ToString()!);
        results["r1"].Should().Contain("Older tool result shortened").And.HaveLength(results["r2"].Length);
        results["r1"].Length.Should().BeLessThan(600);
        results["r3"].Should().HaveLength(3000);
        results["r6"].Should().HaveLength(3000);

        var saved = await store.LoadAsync("tools", TestContext.Current.CancellationToken);
        saved[2].Contents.OfType<FunctionResultContent>().First().Result!.ToString().Should().HaveLength(3000);
    }

    [Fact]
    public async Task RunTurnAsync_SummaryCallFails_ContinuesWithoutCompacting()
    {
        var store = new JsonAgentSessionStore(Path.Combine(_directory, "sessions"));
        await store.SaveAsync("long", ThreeLongTurns(), TestContext.Current.CancellationToken);
        var chat = new ScriptedChatClient([new TextContent("Answer.")]) { SummaryFailure = new InvalidOperationException("rate limited") };
        var runtime = CreateRuntime(chat, [], store: store, contextTokenBudget: 6000);

        var events = await CollectAsync(runtime, "long", "fourth");

        events.OfType<AgentContextCompacted>().Should().BeEmpty();
        events.Last().Should().BeOfType<AgentTurnCompleted>().Which.FinalText.Should().Be("Answer.");
        chat.Requests.Single().Where(m => m.Role == ChatRole.User).Select(m => m.Text)
            .Should().Equal("first", "second", "third", "fourth");
    }

    [Fact]
    public async Task RunTurnAsync_CompactCommand_SummarizesWholeThreadWithoutCallingTools()
    {
        var store = new JsonAgentSessionStore(Path.Combine(_directory, "sessions"));
        await store.SaveAsync(
            "short",
            [new(ChatRole.User, "first"), new(ChatRole.Assistant, "hello")],
            TestContext.Current.CancellationToken);
        var chat = new ScriptedChatClient([new TextContent("Next answer.")]) { Summary = "User said first." };
        var runtime = CreateRuntime(chat, [], store: store);

        var events = await CollectAsync(runtime, "short", " /compact ");

        events.OfType<AgentContextCompacted>().Should().ContainSingle().Which.SummarizedMessageCount.Should().Be(2);
        events.Last().Should().BeOfType<AgentTurnCompleted>().Which.FinalText.Should().StartWith("Compacted");
        chat.Requests.Should().BeEmpty();
        (await store.LoadAsync("short", TestContext.Current.CancellationToken)).Select(m => m.Role)
            .Should().Equal(ChatRole.User, ChatRole.Assistant, ChatRole.System);

        await CollectAsync(runtime, "short", "next");

        var request = chat.Requests.Single();
        request.Select(m => m.Role).Should().Equal(ChatRole.System, ChatRole.User);
        request[0].Text.Should().Contain("User said first.");
        request[1].Text.Should().Be("next");
    }

    [Fact]
    public async Task RunTurnAsync_CompactCommandOnEmptyThread_SaysThereIsNothingToCompact()
    {
        var chat = new ScriptedChatClient() { Summary = "unused" };
        var runtime = CreateRuntime(chat, []);

        var events = await CollectAsync(runtime, "empty", "/compact");

        events.Should().ContainSingle().Which.Should().BeOfType<AgentTurnCompleted>()
            .Which.FinalText.Should().Be("There is nothing to compact yet.");
        chat.SummaryRequests.Should().BeEmpty();
    }

    private static List<ChatMessage> ThreeLongTurns() =>
    [
        new(ChatRole.User, "first"),
        new(ChatRole.Assistant, new string('a', 8000)),
        new(ChatRole.User, "second"),
        new(ChatRole.Assistant, new string('b', 8000)),
        new(ChatRole.User, "third"),
        new(ChatRole.Assistant, new string('c', 8000))
    ];

    private AgentRuntime CreateRuntime(
        IChatClient chat,
        IReadOnlyList<AgentToolDescriptor> tools,
        IToolPermissionService? permissions = null,
        IAgentSessionStore? store = null,
        int maxIterations = 24,
        int maxResultCharacters = 16000,
        int contextTokenBudget = 64000)
    {
        var services = new ServiceCollection();
        services.AddScoped<IAgentToolProvider>(_ => new StaticToolProvider(tools));
        var provider = services.BuildServiceProvider();

        return new AgentRuntime(
            () => chat,
            provider.GetRequiredService<IServiceScopeFactory>(),
            permissions ?? new ToolPermissionService(Path.Combine(_directory, "permissions.json")),
            store ?? new JsonAgentSessionStore(Path.Combine(_directory, "sessions")),
            Options.Create(new AgentRuntimeOptions
            {
                MaxIterations = maxIterations,
                MaxToolResultCharacters = maxResultCharacters,
                ContextTokenBudget = contextTokenBudget
            }),
            NullLogger<AgentRuntime>.Instance,
            () => new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
    }

    private static async Task<List<AgentEvent>> CollectAsync(
        IAgentRuntime runtime,
        string sessionId,
        string message,
        bool approve = true,
        bool alwaysAllow = false)
    {
        var events = new List<AgentEvent>();
        await foreach (var agentEvent in runtime.RunTurnAsync(sessionId, message, TestContext.Current.CancellationToken))
        {
            events.Add(agentEvent);
            if (agentEvent is AgentApprovalRequested request)
            {
                runtime.ResolveApproval(request.RequestId, approve, alwaysAllow);
            }
        }

        return events;
    }

    private static AgentToolDescriptor Tool(AIFunction function, ToolRisk risk) =>
        new(function, risk, "test", function.Name);

    private static AIContent Call(string callId, string name, params (string Key, object? Value)[] arguments) =>
        new FunctionCallContent(callId, name, arguments.ToDictionary(a => a.Key, a => a.Value));

    private sealed class StaticToolProvider(IReadOnlyList<AgentToolDescriptor> tools) : IAgentToolProvider
    {
        public Task<IReadOnlyList<AgentToolDescriptor>> GetToolsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(tools);
    }

    private sealed class ScriptedChatClient(params IList<AIContent>[] responses) : IChatClient
    {
        private readonly Queue<IList<AIContent>> _responses = new(responses);

        public List<List<ChatMessage>> Requests { get; } = [];

        public Exception? Failure { get; init; }

        public string? Summary { get; init; }

        public Exception? SummaryFailure { get; init; }

        public List<List<ChatMessage>> SummaryRequests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            SummaryRequests.Add(messages.ToList());
            if (SummaryFailure is not null)
            {
                throw SummaryFailure;
            }

            return Summary is null
                ? throw new NotSupportedException()
                : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Summary)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(messages.ToList());
            if (Failure is not null)
            {
                throw Failure;
            }

            await Task.Yield();
            foreach (var content in _responses.Dequeue())
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [content]) { MessageId = $"m{Requests.Count}" };
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
