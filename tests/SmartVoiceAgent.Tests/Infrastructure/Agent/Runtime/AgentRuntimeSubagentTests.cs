using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Infrastructure.Agent.Runtime;
using SmartVoiceAgent.Infrastructure.Agent.Subagents;
using SmartVoiceAgent.Tests.Infrastructure.Agent.CliAgents;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Runtime;

public sealed class AgentRuntimeSubagentTests : IDisposable
{
    private const string SubagentMarker = "## You are a subagent";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kam-subagent-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Task_RunsAnExplorerWithReadToolsAndReturnsItsReport()
    {
        var reads = new List<string>();
        var read = Tool(AIFunctionFactory.Create((string path) => { reads.Add(path); return "contents of " + path; }, "files_read"), ToolRisk.Read);
        var write = Tool(AIFunctionFactory.Create((string path) => "written", "files_write"), ToolRisk.Write);
        var chat = new RoutingChatClient(request => (IsSubagent(request), ToolResults(request).Count) switch
        {
            (false, 0) => [Call("t1", "task", ("subagent_type", "explorer"), ("description", "Find the notes"), ("prompt", "Read notes.txt and report."))],
            (true, 0) => [new TextContent("Looking around. "), Call("c1", "files_read", ("path", "notes.txt"))],
            (true, _) => [new TextContent("notes.txt holds the plan.")],
            _ => [new TextContent("The explorer says notes.txt holds the plan.")]
        });
        var tracker = new SubagentRunTracker(Path.Combine(_directory, "runs.jsonl"));
        var runtime = CreateRuntime(chat, [read, write], tracker);

        var events = await CollectAsync(runtime, "s1", "what is in my notes?");

        reads.Should().Equal("notes.txt");
        var started = events.OfType<AgentSubagentStarted>().Should().ContainSingle().Subject;
        started.Should().Match<AgentSubagentStarted>(e => e.SessionId == "s1" && e.CallId == "t1" && e.AgentType == "explorer" && e.Description == "Find the notes");
        events.OfType<AgentSubagentActivity>().Select(e => e.Inner.GetType().Name)
            .Should().Equal(nameof(AgentToolCallStarted), nameof(AgentToolCallCompleted));
        events.OfType<AgentSubagentActivity>().Should().OnlyContain(e => e.RunId == started.RunId && e.CallId == "t1");
        events.OfType<AgentSubagentCompleted>().Should().ContainSingle(e => e.Status == SubagentRunStatus.Completed && e.ToolCallCount == 1);
        events.OfType<AgentToolCallStarted>().Should().ContainSingle(e => e.ToolName == "task" && e.Risk == ToolRisk.Agent);
        events.OfType<AgentToolCallCompleted>().Should().ContainSingle(e => e.ToolName == "task" && e.Success && e.Summary == "notes.txt holds the plan.");
        events.OfType<AgentApprovalRequested>().Should().BeEmpty("starting a subagent and its reads need no approval");
        string.Concat(events.OfType<AgentTextDelta>().Select(e => e.Text))
            .Should().Be("The explorer says notes.txt holds the plan.", "the subagent's own text stays out of the chat");

        var childRequest = chat.Requests.First(IsSubagent);
        childRequest[0].Text.Should().Contain("explorer subagent");
        childRequest[1].Text.Should().Be("Read notes.txt and report.");
        chat.ToolNames(childRequest).Should().Equal("files_read");
        chat.ToolNames(chat.Requests[0]).Should().Contain(["files_read", "files_write", "task"]);
        var parentResult = chat.Requests.Last().SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        parentResult.CallId.Should().Be("t1");
        parentResult.Result!.ToString().Should().Be("notes.txt holds the plan.");

        var run = tracker.Get(started.RunId)!;
        run.Status.Should().Be(SubagentRunStatus.Completed);
        run.ParentSessionId.Should().Be("s1");
        run.Result.Should().Be("notes.txt holds the plan.");
        run.StepList.Should().ContainSingle(step => step.ToolName == "files_read" && step.Status == SubagentStepStatus.Succeeded && step.Summary == "contents of notes.txt");
        new SubagentRunTracker(Path.Combine(_directory, "runs.jsonl")).Get(started.RunId)
            .Should().Match<SubagentRun>(saved => saved.Status == SubagentRunStatus.Completed && saved.StepList.Count == 1, "finished runs are saved");
    }

    [Fact]
    public async Task Task_GeneralSubagentAsksForApprovalThroughTheParentTurn()
    {
        var writes = 0;
        var write = Tool(AIFunctionFactory.Create((string path) => { writes++; return "written"; }, "files_write"), ToolRisk.Write);
        var chat = new RoutingChatClient(request => (IsSubagent(request), ToolResults(request).Count) switch
        {
            (false, 0) => [Call("t1", "task", ("subagent_type", "general"), ("description", "Write the file"), ("prompt", "Write out.txt."))],
            (true, 0) => [Call("c1", "files_write", ("path", "out.txt"))],
            (true, _) => [new TextContent("Wrote out.txt.")],
            _ => [new TextContent("Done.")]
        });
        var runtime = CreateRuntime(chat, [write], new SubagentRunTracker(path: null));

        var events = await CollectAsync(runtime, "s1", "write out.txt");

        writes.Should().Be(1);
        var approval = events.OfType<AgentSubagentActivity>().Select(e => e.Inner).OfType<AgentApprovalRequested>().Should().ContainSingle().Subject;
        approval.ToolName.Should().Be("files_write");
        events.OfType<AgentApprovalRequested>().Should().BeEmpty("the approval arrives wrapped, tied to the task call");
        events.Last().Should().BeOfType<AgentTurnCompleted>();
    }

    [Fact]
    public async Task Task_ReadOnlySubagentsInOneResponseRunTogether()
    {
        var probe = new Gate(expected: 2);
        var read = Tool(AIFunctionFactory.Create((string path, CancellationToken token) => probe.RunAsync(path, token), "files_read"), ToolRisk.Read);
        var chat = new RoutingChatClient(request => (IsSubagent(request), ToolResults(request).Count) switch
        {
            (false, 0) =>
            [
                Call("t1", "task", ("subagent_type", "explorer"), ("description", "A"), ("prompt", "Read a.txt")),
                Call("t2", "task", ("subagent_type", "researcher"), ("description", "B"), ("prompt", "Read b.txt"))
            ],
            (true, 0) => [Call("c-" + request[1].Text, "files_read", ("path", request[1].Text.Split(' ')[1]))],
            (true, _) => [new TextContent("ok")],
            _ => [new TextContent("Both done.")]
        });
        var runtime = CreateRuntime(chat, [read], new SubagentRunTracker(path: null));

        var events = await CollectAsync(runtime, "s1", "read both");

        probe.MaxRunning.Should().Be(2);
        events.OfType<AgentSubagentCompleted>().Should().HaveCount(2).And.OnlyContain(e => e.Status == SubagentRunStatus.Completed);
        events.Last().Should().BeOfType<AgentTurnCompleted>();
    }

    [Fact]
    public async Task Task_StoppedFromTheTracker_ReturnsANoteAndTheTurnGoesOn()
    {
        var tracker = new SubagentRunTracker(path: null);
        var read = Tool(
            AIFunctionFactory.Create(async (CancellationToken token) => { await Task.Delay(Timeout.Infinite, token); return "never"; }, "slow_read"),
            ToolRisk.Read);
        tracker.Changed += (_, run) =>
        {
            if (run.StepList.Any(step => step.Status == SubagentStepStatus.Running))
            {
                tracker.Cancel(run.RunId);
            }
        };
        var chat = new RoutingChatClient(request => (IsSubagent(request), ToolResults(request).Count) switch
        {
            (false, 0) => [Call("t1", "task", ("subagent_type", "explorer"), ("description", "Slow"), ("prompt", "Read slowly"))],
            (true, _) => [Call("c1", "slow_read")],
            _ => [new TextContent("Carried on.")]
        });
        var runtime = CreateRuntime(chat, [read], tracker);

        var events = await CollectAsync(runtime, "s1", "go");

        events.OfType<AgentSubagentCompleted>().Should().ContainSingle(e => e.Status == SubagentRunStatus.Cancelled);
        var taskResult = events.OfType<AgentToolCallCompleted>().Single(e => e.ToolName == "task");
        taskResult.Summary.Should().StartWith("Error: the user stopped this subagent");
        taskResult.Success.Should().BeFalse();
        events.Last().Should().BeOfType<AgentTurnCompleted>().Which.FinalText.Should().Be("Carried on.");
        var run = tracker.List("s1").Single();
        run.Status.Should().Be(SubagentRunStatus.Cancelled);
        run.StepList.Single().Status.Should().Be(SubagentStepStatus.Failed);
    }

    [Fact]
    public async Task Task_ApprovedStepRunsAgainInTheTracker()
    {
        var tracker = new SubagentRunTracker(path: null);
        var statuses = new List<SubagentStepStatus>();
        tracker.Changed += (_, run) =>
        {
            if (run.StepList.Count == 1 && (statuses.Count == 0 || statuses[^1] != run.StepList[0].Status))
            {
                statuses.Add(run.StepList[0].Status);
            }
        };
        var write = Tool(AIFunctionFactory.Create((string path) => "written", "files_write"), ToolRisk.Write);
        var chat = new RoutingChatClient(request => (IsSubagent(request), ToolResults(request).Count) switch
        {
            (false, 0) => [Call("t1", "task", ("subagent_type", "general"), ("description", "Write"), ("prompt", "Write out.txt."))],
            (true, 0) => [Call("c1", "files_write", ("path", "out.txt"))],
            (true, _) => [new TextContent("Wrote it.")],
            _ => [new TextContent("Done.")]
        });

        await CollectAsync(CreateRuntime(chat, [write], tracker), "s1", "write");

        statuses.Should().Equal(SubagentStepStatus.Running, SubagentStepStatus.AwaitingApproval, SubagentStepStatus.Running, SubagentStepStatus.Succeeded);
    }

    [Fact]
    public async Task Task_StoppedWhileWaitingForApproval_EndsTheRunAndTheTurnGoesOn()
    {
        var tracker = new SubagentRunTracker(path: null);
        tracker.Changed += (_, run) =>
        {
            if (run.IsWaitingForApproval)
            {
                tracker.Cancel(run.RunId);
            }
        };
        var write = Tool(AIFunctionFactory.Create((string path) => "written", "files_write"), ToolRisk.Write);
        var chat = new RoutingChatClient(request => (IsSubagent(request), ToolResults(request).Count) switch
        {
            (false, 0) => [Call("t1", "task", ("subagent_type", "general"), ("description", "Write"), ("prompt", "Write out.txt."))],
            (true, _) => [Call("c1", "files_write", ("path", "out.txt"))],
            _ => [new TextContent("Carried on.")]
        });

        var events = await CollectAsync(CreateRuntime(chat, [write], tracker), "s1", "write", approve: false);

        events.OfType<AgentSubagentCompleted>().Should().ContainSingle(e => e.Status == SubagentRunStatus.Cancelled);
        events.Last().Should().BeOfType<AgentTurnCompleted>().Which.FinalText.Should().Be("Carried on.");
        tracker.List().Single().StepList.Single().Status.Should().Be(SubagentStepStatus.Failed);
    }

    [Fact]
    public async Task Task_StoppingTheTurnStopsItsSubagents()
    {
        var tracker = new SubagentRunTracker(path: null);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = Tool(
            AIFunctionFactory.Create(async (CancellationToken token) => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return "never"; }, "slow_read"),
            ToolRisk.Read);
        var chat = new RoutingChatClient(request => IsSubagent(request)
            ? [Call("c1", "slow_read")]
            : [Call("t1", "task", ("subagent_type", "explorer"), ("description", "A"), ("prompt", "a")), Call("t2", "task", ("subagent_type", "explorer"), ("description", "B"), ("prompt", "b"))]);
        var runtime = CreateRuntime(chat, [read], tracker);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var events = new List<AgentEvent>();

        var turn = Task.Run(async () =>
        {
            await foreach (var agentEvent in runtime.RunTurnAsync("s1", "go", stop.Token))
            {
                events.Add(agentEvent);
            }
        }, TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        stop.Cancel();
        await turn.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        events.Last().Should().BeOfType<AgentTurnFailed>().Which.Message.Should().Be("Stopped.");
        events.OfType<AgentSubagentCompleted>().Should().HaveCount(2).And.OnlyContain(e => e.Status == SubagentRunStatus.Cancelled);
        tracker.List().Should().HaveCount(2).And.OnlyContain(run => run.Status == SubagentRunStatus.Cancelled && run.CompletedAt != null);
    }

    [Fact]
    public async Task Task_AGeneralSubagentWaitsForTheReadOnlyOnesBeforeIt()
    {
        var order = new List<string>();
        var read = Tool(AIFunctionFactory.Create(async (string path) => { lock (order) { order.Add("read start"); } await Task.Delay(150); lock (order) { order.Add("read end"); } return "ok"; }, "files_read"), ToolRisk.Read);
        var write = Tool(AIFunctionFactory.Create((string path) => { lock (order) { order.Add("write"); } return "ok"; }, "files_write"), ToolRisk.Write);
        var chat = new RoutingChatClient(request => (IsSubagent(request), ToolResults(request).Count) switch
        {
            (false, 0) =>
            [
                Call("t1", "task", ("subagent_type", "explorer"), ("description", "Read"), ("prompt", "read")),
                Call("t2", "task", ("subagent_type", "general"), ("description", "Write"), ("prompt", "write"))
            ],
            (true, 0) when request[1].Text == "read" => [Call("c1", "files_read", ("path", "a"))],
            (true, 0) => [Call("c1", "files_write", ("path", "b"))],
            (true, _) => [new TextContent("ok")],
            _ => [new TextContent("Done.")]
        });

        await CollectAsync(CreateRuntime(chat, [read, write], new SubagentRunTracker(path: null)), "s1", "go");

        order.Should().Equal("read start", "read end", "write");
    }

    [Fact]
    public void Tracker_SkipsBrokenLinesKeepsTheNewestCopyAndCapsTheFile()
    {
        var path = Path.Combine(_directory, "runs.jsonl");
        var writer = new SubagentRunTracker(path);
        for (var index = 0; index < 502; index++)
        {
            var run = new SubagentRun($"r{index}", "s1", "c", "explorer", "d", "p", SubagentRunStatus.Running, DateTimeOffset.Now);
            writer.Start(run, new CancellationTokenSource());
            writer.Finish(run.RunId, current => current with { Status = SubagentRunStatus.Completed, Result = "first" });
        }

        writer.Finish("r501", current => current with { Result = "second" });
        File.AppendAllText(path, "{\"runId\":\"broken");

        var reader = new SubagentRunTracker(path);

        reader.List(maxCount: 1000).Should().HaveCount(500);
        reader.Get("r501")!.Result.Should().Be("second", "the newest line for a run wins");
        reader.Get("r0").Should().BeNull("runs past the cap are dropped");
        File.ReadAllLines(path).Should().HaveCount(500, "the file is rewritten without the dropped runs");
    }

    [Fact]
    public void Tracker_ASubscriberThatThrowsDoesNotStopOthers()
    {
        var tracker = new SubagentRunTracker(path: null);
        var seen = 0;
        tracker.Changed += (_, _) => throw new InvalidOperationException("broken view");
        tracker.Changed += (_, _) => seen++;

        tracker.Start(new SubagentRun("r1", "s1", "c", "explorer", "d", "p", SubagentRunStatus.Running, DateTimeOffset.Now), new CancellationTokenSource());

        seen.Should().Be(1);
    }

    [Fact]
    public async Task Task_UnknownTypeReturnsAnErrorWithoutStartingARun()
    {
        var tracker = new SubagentRunTracker(path: null);
        var chat = new RoutingChatClient(request => ToolResults(request).Count == 0
            ? [Call("t1", "task", ("subagent_type", "wizard"), ("description", "x"), ("prompt", "y"))]
            : [new TextContent("ok")]);
        var runtime = CreateRuntime(chat, [], tracker);

        var events = await CollectAsync(runtime, "s1", "go");

        events.OfType<AgentToolCallCompleted>().Single().Summary.Should().StartWith("Error: there is no subagent type 'wizard'");
        events.OfType<AgentSubagentStarted>().Should().BeEmpty();
        tracker.List().Should().BeEmpty();
    }

    [Fact]
    public void Catalog_ReadOnlyTypesNeverGetWriteToolsOrTheTaskTool()
    {
        var write = Tool(AIFunctionFactory.Create(() => "x", "w"), ToolRisk.Write);
        var web = Tool(AIFunctionFactory.Create(() => "x", "web"), ToolRisk.Network);
        var task = Tool(AIFunctionFactory.Create(() => "x", "task"), ToolRisk.Agent);

        SubagentCatalog.Explorer.Allows(web).Should().BeFalse();
        SubagentCatalog.Researcher.Allows(web).Should().BeTrue();
        SubagentCatalog.Researcher.Allows(write).Should().BeFalse();
        SubagentCatalog.General.Allows(write).Should().BeTrue();
        SubagentCatalog.All.Should().OnlyContain(definition => !definition.Allows(task));
    }

    private static bool IsSubagent(IReadOnlyList<ChatMessage> request) => request[0].Text.Contains(SubagentMarker, StringComparison.Ordinal);

    private static List<FunctionResultContent> ToolResults(IReadOnlyList<ChatMessage> request) =>
        request.SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToList();

    private AgentRuntime CreateRuntime(IChatClient chat, IReadOnlyList<AgentToolDescriptor> tools, ISubagentRunTracker tracker)
    {
        var services = new ServiceCollection();
        services.AddScoped<IAgentToolProvider>(_ => new StaticToolProvider(tools));
        var provider = services.BuildServiceProvider();

        return new AgentRuntime(
            _ => chat,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new ToolPermissionService(Path.Combine(_directory, "permissions.json")),
            new JsonAgentSessionStore(Path.Combine(_directory, "sessions")),
            new StaticOptionsMonitor<AgentRuntimeOptions>(new AgentRuntimeOptions { ModelRetryBaseDelayMilliseconds = 5 }),
            NullLogger<AgentRuntime>.Instance,
            () => new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero),
            subagents: tracker);
    }

    private static async Task<List<AgentEvent>> CollectAsync(IAgentRuntime runtime, string sessionId, string message, bool approve = true)
    {
        var events = new List<AgentEvent>();
        await foreach (var agentEvent in runtime.RunTurnAsync(sessionId, message, TestContext.Current.CancellationToken))
        {
            events.Add(agentEvent);
            var approval = agentEvent as AgentApprovalRequested ?? (agentEvent as AgentSubagentActivity)?.Inner as AgentApprovalRequested;
            if (approval is not null && approve)
            {
                runtime.ResolveApproval(approval.RequestId, approved: true);
            }
        }

        return events;
    }

    private static AgentToolDescriptor Tool(AIFunction function, ToolRisk risk) => new(function, risk, "test", function.Name);

    private static AIContent Call(string callId, string name, params (string Key, object? Value)[] arguments) =>
        new FunctionCallContent(callId, name, arguments.ToDictionary(a => a.Key, a => a.Value));

    private sealed class StaticToolProvider(IReadOnlyList<AgentToolDescriptor> tools) : IAgentToolProvider
    {
        public Task<IReadOnlyList<AgentToolDescriptor>> GetToolsAsync(CancellationToken cancellationToken = default) => Task.FromResult(tools);
    }

    /// <summary>Waits until the expected number of calls run at once, or a short time when they run one by one.</summary>
    private sealed class Gate(int expected)
    {
        private readonly TaskCompletionSource _together = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _lock = new();
        private int _running;

        public int MaxRunning { get; private set; }

        public async Task<string> RunAsync(string name, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _running++;
                MaxRunning = Math.Max(MaxRunning, _running);
                if (_running >= expected)
                {
                    _together.TrySetResult();
                }
            }

            await Task.WhenAny(_together.Task, Task.Delay(1000, cancellationToken));
            lock (_lock)
            {
                _running--;
            }

            return "read " + name;
        }
    }

    /// <summary>Answers each streaming request with what the responder returns for its messages.</summary>
    private sealed class RoutingChatClient(Func<IReadOnlyList<ChatMessage>, IList<AIContent>> responder) : IChatClient
    {
        private readonly object _lock = new();
        private readonly Dictionary<List<ChatMessage>, ChatOptions?> _options = new(ReferenceEqualityComparer.Instance);

        public List<List<ChatMessage>> Requests { get; } = [];

        public IReadOnlyList<string> ToolNames(List<ChatMessage> request)
        {
            lock (_lock)
            {
                return _options[request]?.Tools?.Select(tool => tool.Name).ToList() ?? [];
            }
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var request = messages.ToList();
            lock (_lock)
            {
                Requests.Add(request);
                _options[request] = options;
            }

            await Task.Yield();
            foreach (var content in responder(request))
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [content]);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
