using System.Net;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Infrastructure.Agent.Runtime;
using SmartVoiceAgent.Tests.Infrastructure.Agent.CliAgents;

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
    public async Task RunTurnAsync_ReportsTheLastRequestSizeAsTheContext()
    {
        var tool = Tool(AIFunctionFactory.Create((string path) => "hello", "files_read"), ToolRisk.Read);
        var chat = new ScriptedChatClient(
            [Call("call-1", "files_read", ("path", "notes.txt")), new UsageContent(new UsageDetails { InputTokenCount = 30, OutputTokenCount = 5 })],
            [new TextContent("Done."), new UsageContent(new UsageDetails { InputTokenCount = 45, OutputTokenCount = 2 })]);
        var runtime = CreateRuntime(chat, [tool]);

        var events = await CollectAsync(runtime, "s1", "read notes.txt");

        var completed = events.Last().Should().BeOfType<AgentTurnCompleted>().Subject;
        completed.InputTokens.Should().Be(75, "the turn total counts every request");
        completed.ContextTokens.Should().Be(45, "the context is what the last request sent");
    }

    [Fact]
    public async Task RunTurnAsync_UsesTheModelChosenForTheThread()
    {
        var store = new JsonAgentSessionStore(Path.Combine(_directory, "sessions"));
        var requestedModels = new List<string?>();
        var chat = new ScriptedChatClient([new TextContent("one")], [new TextContent("two")]);
        var runtime = CreateRuntime(chat, [], store: store, chatFactory: model =>
        {
            requestedModels.Add(model);
            return chat;
        });

        await CollectAsync(runtime, "s1", "first");
        await store.SetModelAsync("s1", "vendor/model-b", TestContext.Current.CancellationToken);
        await CollectAsync(runtime, "s1", "second");

        requestedModels.Distinct().Should().Equal(null, "vendor/model-b");
    }

    [Fact]
    public async Task RunTurnAsync_PromptContributors_AppendSectionsToSystemPrompt()
    {
        var chat = new ScriptedChatClient([new TextContent("ok")]);
        var runtime = CreateRuntime(chat, [], contributors:
        [
            new FixedPromptContributor("Skills:\n- pdf: Extract PDF text"),
            new FixedPromptContributor(null),
            new FixedPromptContributor(throws: true)
        ]);

        var events = await CollectAsync(runtime, "s1", "hi");

        events.Last().Should().BeOfType<AgentTurnCompleted>();
        chat.Requests.Single()[0].Text.Should().EndWith("Skills:\n- pdf: Extract PDF text");
    }

    [Fact]
    public async Task RunTurnAsync_WithWorkspace_TellsTheModelWhichFolderToUse()
    {
        var chat = new ScriptedChatClient([new TextContent("ok")]);
        var runtime = CreateRuntime(chat, [], workspaceRoot: @"C:\Projects\Kam");

        await CollectAsync(runtime, "s1", "review the project");

        var prompt = chat.Requests.Single()[0].Text;
        prompt.Should().Contain(@"The user's workspace folder is C:\Projects\Kam.");
        prompt.Should().NotContain("No workspace folder is selected");
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
        permissions.AllowRules.Should().Contain("shell_run");
    }

    [Fact]
    public async Task RunTurnAsync_DelegateTool_AsksInFullAutoAndAlwaysAllowSavesNothing()
    {
        var runs = 0;
        var tool = Tool(AIFunctionFactory.Create(() => { runs++; return "done"; }, "cli_agent_run"), ToolRisk.Delegate);
        var permissions = new ToolPermissionService(Path.Combine(_directory, "permissions.json"), ApprovalMode.FullAuto);
        var chat = new ScriptedChatClient(
            [Call("call-1", "cli_agent_run")],
            [new TextContent("first")],
            [Call("call-2", "cli_agent_run")],
            [new TextContent("second")]);
        var runtime = CreateRuntime(chat, [tool], permissions);

        var first = await CollectAsync(runtime, "s1", "delegate", approve: true, alwaysAllow: true);
        var second = await CollectAsync(runtime, "s1", "delegate again");

        first.OfType<AgentApprovalRequested>().Should().ContainSingle(request => request.AlwaysAllowRule == string.Empty);
        second.OfType<AgentApprovalRequested>().Should().ContainSingle();
        permissions.AllowRules.Should().BeEmpty();
        runs.Should().Be(2);
    }

    [Fact]
    public async Task RunTurnAsync_PrepareArguments_ApprovalShowsAndToolGetsTheFilledArguments()
    {
        string? ranIn = null;
        var function = AIFunctionFactory.Create((string task, string workspace) => { ranIn = workspace; return "done"; }, "cli_agent_run");
        var tool = new AgentToolDescriptor(function, ToolRisk.Delegate, "test", "Delegate", arguments =>
            new Dictionary<string, object?>(arguments) { ["workspace"] = @"C:\repo", ["sandbox"] = "read-only" });
        var chat = new ScriptedChatClient(
            [Call("call-1", "cli_agent_run", ("task", "Explain"))],
            [new TextContent("done")]);
        var runtime = CreateRuntime(chat, [tool]);

        var events = await CollectAsync(runtime, "s1", "delegate", approve: true);

        var approval = events.OfType<AgentApprovalRequested>().Single();
        approval.ArgumentsJson.Should().Contain("C:\\\\repo").And.Contain("read-only");
        ranIn.Should().Be(@"C:\repo", "the call runs with the arguments the user approved");
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
    public async Task RunTurnAsync_ChangedLimits_ApplyToTheNextTurn()
    {
        var tool = Tool(AIFunctionFactory.Create(() => "again", "loop_tool"), ToolRisk.Read);
        var options = new MutableOptionsMonitor(new AgentRuntimeOptions { MaxIterations = 2 });
        var chat = new ScriptedChatClient(Enumerable.Range(0, 20).Select(i => (IList<AIContent>)[Call($"c{i}", "loop_tool")]).ToArray());
        var runtime = CreateRuntime(chat, [tool], options: options);

        await CollectAsync(runtime, "s1", "loop");
        chat.Requests.Should().HaveCount(2);

        options.CurrentValue = new AgentRuntimeOptions { MaxIterations = 5 };
        await CollectAsync(runtime, "s2", "loop");
        chat.Requests.Should().HaveCount(2 + 5);
    }

    [Fact]
    public async Task RunTurnAsync_OutOfRangeLimits_AreClamped()
    {
        var tool = Tool(AIFunctionFactory.Create(() => "again", "loop_tool"), ToolRisk.Read);
        var chat = new ScriptedChatClient(Enumerable.Range(0, 300).Select(i => (IList<AIContent>)[Call($"c{i}", "loop_tool")]).ToArray());
        var runtime = CreateRuntime(chat, [tool], options: new MutableOptionsMonitor(new AgentRuntimeOptions { MaxIterations = 100000 }));

        var events = await CollectAsync(runtime, "s1", "loop");

        chat.Requests.Should().HaveCount(200);
        events.Last().Should().BeOfType<AgentTurnFailed>().Which.Message.Should().Contain("200 steps");
    }

    [Fact]
    public async Task RunTurnAsync_LongToolResult_IsTruncatedForModel()
    {
        var tool = Tool(AIFunctionFactory.Create(() => new string('x', 1500), "big"), ToolRisk.Read);
        var chat = new ScriptedChatClient([Call("c1", "big")], [new TextContent("done")]);
        var runtime = CreateRuntime(chat, [tool], maxResultCharacters: 1000);

        await CollectAsync(runtime, "s1", "go");

        var result = chat.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single().Result!.ToString()!;
        result.Should().StartWith(new string('x', 1000)).And.Contain("[truncated 500 characters]");
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
        var runtime = CreateRuntime(chat, [], store: store, contextTokenBudget: 8000);

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
                new(ChatRole.Tool, callIds.Select(id => (AIContent)new FunctionResultContent(id, new string('x', 6000))).ToList()),
                new(ChatRole.Assistant, "All read.")
            ],
            TestContext.Current.CancellationToken);
        var chat = new ScriptedChatClient([new TextContent("Done.")]) { Summary = "unused" };
        var runtime = CreateRuntime(chat, [], store: store, contextTokenBudget: 8000);

        var events = await CollectAsync(runtime, "tools", "again");

        events.OfType<AgentContextCompacted>().Should().BeEmpty();
        chat.SummaryRequests.Should().BeEmpty();
        var results = chat.Requests.Single().SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .ToDictionary(r => r.CallId, r => r.Result!.ToString()!);
        results["r1"].Should().Contain("Older tool result shortened").And.HaveLength(results["r2"].Length);
        results["r1"].Length.Should().BeLessThan(600);
        results["r3"].Should().HaveLength(6000);
        results["r6"].Should().HaveLength(6000);

        var saved = await store.LoadAsync("tools", TestContext.Current.CancellationToken);
        saved[2].Contents.OfType<FunctionResultContent>().First().Result!.ToString().Should().HaveLength(6000);
    }

    [Fact]
    public async Task RunTurnAsync_SummaryCallFails_ContinuesWithoutCompacting()
    {
        var store = new JsonAgentSessionStore(Path.Combine(_directory, "sessions"));
        await store.SaveAsync("long", ThreeLongTurns(), TestContext.Current.CancellationToken);
        var chat = new ScriptedChatClient([new TextContent("Answer.")]) { SummaryFailure = new InvalidOperationException("rate limited") };
        var runtime = CreateRuntime(chat, [], store: store, contextTokenBudget: 8000);

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

    [Fact]
    public async Task RunTurnAsync_TransientModelError_RetriesAndCompletes()
    {
        var chat = new ScriptedChatClient(
            [Fail(new HttpRequestException("Service Unavailable", null, HttpStatusCode.ServiceUnavailable))],
            [new TextContent("Back again.")]);
        var runtime = CreateRuntime(chat, []);

        var events = await CollectAsync(runtime, "s1", "hi");

        chat.Requests.Should().HaveCount(2);
        events.Last().Should().BeOfType<AgentTurnCompleted>().Which.FinalText.Should().Be("Back again.");
    }

    [Theory]
    [InlineData(408, "request timed out (HTTP 408)")]
    [InlineData(429, "rate limited (HTTP 429)")]
    [InlineData(500, "provider error (HTTP 500)")]
    [InlineData(502, "provider error (HTTP 502)")]
    [InlineData(503, "provider error (HTTP 503)")]
    [InlineData(504, "provider error (HTTP 504)")]
    [InlineData(529, "provider error (HTTP 529)")]
    public async Task RunTurnAsync_TransientStatus_ReportsTheRetryInTheEventStream(int status, string reason)
    {
        var chat = new ScriptedChatClient(
            [Fail(new HttpRequestException("failed", null, (HttpStatusCode)status))],
            [new TextContent("ok")]);
        var runtime = CreateRuntime(chat, [], maxModelRetries: 2);

        var events = await CollectAsync(runtime, "s1", "hi");

        events.OfType<AgentModelRetryScheduled>().Should().ContainSingle().Which.Should().Be(
            new AgentModelRetryScheduled("s1", 1, 2, TimeSpan.FromMilliseconds(5), reason, PartialOutputDiscarded: false));
        events.Last().Should().BeOfType<AgentTurnCompleted>();
    }

    [Fact]
    public async Task RunTurnAsync_RequestTimesOut_Retries()
    {
        var timeout = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException());
        var chat = new ScriptedChatClient([Fail(timeout)], [new TextContent("ok")]);
        var runtime = CreateRuntime(chat, []);

        var events = await CollectAsync(runtime, "s1", "hi");

        events.OfType<AgentModelRetryScheduled>().Should().ContainSingle().Which.Reason.Should().Be("request timed out");
        events.Last().Should().BeOfType<AgentTurnCompleted>().Which.FinalText.Should().Be("ok");
    }

    [Fact]
    public async Task RunTurnAsync_StreamDropsAfterSomeText_StartsTheRequestOverAndKeepsOnlyTheFullAnswer()
    {
        var store = new JsonAgentSessionStore(Path.Combine(_directory, "sessions"));
        var chat = new ScriptedChatClient(
            [new TextContent("The ans"), Fail(new IOException("The response ended prematurely."))],
            [new TextContent("The answer.")]);
        var runtime = CreateRuntime(chat, [], store: store);

        var events = await CollectAsync(runtime, "s1", "question");

        var retry = events.OfType<AgentModelRetryScheduled>().Should().ContainSingle().Subject;
        retry.Reason.Should().Be("connection lost");
        retry.PartialOutputDiscarded.Should().BeTrue("the UI has to drop the text it already showed");
        events.Last().Should().BeOfType<AgentTurnCompleted>().Which.FinalText.Should().Be("The answer.");

        chat.Requests[1].Select(m => m.Text).Should().Equal(chat.Requests[0].Select(m => m.Text), "the partial reply is not sent back");
        var saved = await store.LoadAsync("s1", TestContext.Current.CancellationToken);
        saved.Select(m => $"{m.Role}:{m.Text}").Should().Equal("user:question", "assistant:The answer.");
    }

    [Fact]
    public async Task RunTurnAsync_TransientErrorAfterAToolCall_RetriesTheRequestWithoutRunningTheToolAgain()
    {
        var runs = 0;
        var tool = Tool(AIFunctionFactory.Create(() => { runs++; return "42"; }, "calc"), ToolRisk.Read);
        var chat = new ScriptedChatClient(
            [Call("c1", "calc")],
            [Fail(new HttpRequestException("failed", null, HttpStatusCode.BadGateway))],
            [new TextContent("It is 42.")]);
        var runtime = CreateRuntime(chat, [tool]);

        var events = await CollectAsync(runtime, "s1", "calculate");

        runs.Should().Be(1);
        chat.Requests.Should().HaveCount(3);
        events.Last().Should().BeOfType<AgentTurnCompleted>().Which.ToolCallCount.Should().Be(1);
    }

    [Fact]
    public async Task RunTurnAsync_ModelKeepsFailing_StopsAtTheRetryCapWithDoublingWaits()
    {
        IList<AIContent> unavailable = [Fail(new HttpRequestException("failed", null, HttpStatusCode.ServiceUnavailable))];
        var chat = new ScriptedChatClient(unavailable, unavailable, unavailable, unavailable, unavailable);
        var runtime = CreateRuntime(chat, [], maxModelRetries: 2);

        var events = await CollectAsync(runtime, "s1", "hi");

        chat.Requests.Should().HaveCount(3, "one request and two retries");
        events.OfType<AgentModelRetryScheduled>().Select(e => (e.Attempt, e.Delay.TotalMilliseconds))
            .Should().Equal((1, 5), (2, 10));
        events.Last().Should().BeOfType<AgentTurnFailed>().Which.Message.Should()
            .Be("The model did not answer after 3 attempts. Last error: provider error (HTTP 503). Check your connection or try again in a moment.");
    }

    [Fact]
    public async Task RunTurnAsync_RetriesTurnedOff_FailsOnTheFirstTransientError()
    {
        var chat = new ScriptedChatClient(
            [Fail(new HttpRequestException("failed", null, HttpStatusCode.ServiceUnavailable))],
            [new TextContent("unreachable")]);
        var runtime = CreateRuntime(chat, [], maxModelRetries: 0);

        var events = await CollectAsync(runtime, "s1", "hi");

        chat.Requests.Should().HaveCount(1);
        events.Should().ContainSingle().Which.Should().BeOfType<AgentTurnFailed>()
            .Which.Message.Should().StartWith("The model request failed: provider error (HTTP 503).");
    }

    [Theory]
    [InlineData(1, 2000, 2000)]
    [InlineData(2, 2000, 4000)]
    [InlineData(3, 2000, 8000)]
    [InlineData(5, 2000, 30000)]
    [InlineData(12, 2000, 30000)]
    [InlineData(1, 0, 0)]
    [InlineData(1, -50, 0)]
    public void ModelRetryDelay_DoublesAndStopsAtThirtySeconds(int retry, int baseMilliseconds, int expectedMilliseconds)
    {
        AgentRuntime.ModelRetryDelay(retry, baseMilliseconds).Should().Be(TimeSpan.FromMilliseconds(expectedMilliseconds));
    }

    [Theory]
    [InlineData(400, "The provider rejected the request (HTTP 400): unsupported parameter")]
    [InlineData(401, "The provider rejected the API key (HTTP 401). Check the key for this model in Settings, then send the message again. Detail: unsupported parameter")]
    [InlineData(403, "The provider rejected the API key (HTTP 403). Check the key for this model in Settings, then send the message again. Detail: unsupported parameter")]
    [InlineData(404, "The provider did not find the model or endpoint (HTTP 404). Check the model name and endpoint in Settings. Detail: unsupported parameter")]
    [InlineData(422, "The provider rejected the request (HTTP 422): unsupported parameter")]
    public async Task RunTurnAsync_ErrorTheUserMustFix_FailsAtOnceWithWhatToDo(int status, string message)
    {
        var chat = new ScriptedChatClient(
            [Fail(new HttpRequestException("unsupported parameter", null, (HttpStatusCode)status))],
            [new TextContent("unreachable")]);
        var runtime = CreateRuntime(chat, []);

        var events = await CollectAsync(runtime, "s1", "hi");

        chat.Requests.Should().HaveCount(1);
        events.Should().ContainSingle().Which.Should().BeOfType<AgentTurnFailed>().Which.Message.Should().Be(message);
    }

    [Fact]
    public async Task RunTurnAsync_ContextTooLong_FailsAtOnceAndPointsToCompact()
    {
        var chat = new ScriptedChatClient(
            [Fail(new HttpRequestException("This model's maximum context length is 8192 tokens.", null, HttpStatusCode.BadRequest))],
            [new TextContent("unreachable")]);
        var runtime = CreateRuntime(chat, []);

        var events = await CollectAsync(runtime, "s1", "hi");

        chat.Requests.Should().HaveCount(1);
        events.Should().ContainSingle().Which.Should().BeOfType<AgentTurnFailed>().Which.Message.Should()
            .Be("The conversation is too long for this model. Send /compact to summarize earlier turns, or start a new task.");
    }

    [Fact]
    public async Task RunTurnAsync_StoppedWhileWaitingToRetry_EndsTheTurnWithoutAnotherRequest()
    {
        var chat = new ScriptedChatClient(
            [Fail(new HttpRequestException("failed", null, HttpStatusCode.ServiceUnavailable))],
            [new TextContent("unreachable")]);
        var runtime = CreateRuntime(chat, [], retryDelayMilliseconds: 25000);
        using var cancellation = new CancellationTokenSource();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var events = new List<AgentEvent>();
        await foreach (var agentEvent in runtime.RunTurnAsync("s1", "hi", cancellation.Token))
        {
            events.Add(agentEvent);
            if (agentEvent is AgentModelRetryScheduled)
            {
                cancellation.Cancel();
            }
        }

        events.Last().Should().BeOfType<AgentTurnFailed>().Which.Message.Should().Be("Stopped.");
        chat.Requests.Should().HaveCount(1);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "Stop does not wait out the 25 second retry delay");
    }

    [Theory]
    [InlineData(ToolRisk.Read)]
    [InlineData(ToolRisk.Network)]
    public async Task RunTurnAsync_ReadOnlyToolCallsInOneResponse_RunTogether(ToolRisk risk)
    {
        var probe = new ConcurrencyProbe(expected: 2, waitMilliseconds: 5000);
        var tool = Tool(AIFunctionFactory.Create(probe.RunAsync, "files_read"), risk);
        var chat = new ScriptedChatClient(
            [Call("c1", "files_read", ("name", "a")), Call("c2", "files_read", ("name", "b"))],
            [new TextContent("Read both.")]);
        var runtime = CreateRuntime(chat, [tool]);

        var events = await CollectAsync(runtime, "s1", "read both");

        probe.MaxRunning.Should().Be(2, "read-only calls from one response do not wait for each other");
        events.Last().Should().BeOfType<AgentTurnCompleted>().Which.ToolCallCount.Should().Be(2);
        chat.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .Select(r => r.CallId).Should().Equal("c1", "c2");
    }

    [Fact]
    public async Task RunTurnAsync_SecondReadFinishesFirst_ResultsStillGoBackInTheOrderTheModelAsked()
    {
        var fastDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tool = Tool(
            AIFunctionFactory.Create(
                async (string name, CancellationToken cancellationToken) =>
                {
                    if (name == "slow")
                    {
                        await fastDone.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                        await Task.Delay(50, cancellationToken);
                    }

                    return "read " + name;
                },
                "files_read"),
            ToolRisk.Read);
        var chat = new ScriptedChatClient(
            [Call("c1", "files_read", ("name", "slow")), Call("c2", "files_read", ("name", "fast"))],
            [new TextContent("Read both.")]);
        var runtime = CreateRuntime(chat, [tool]);

        var events = new List<AgentEvent>();
        await foreach (var agentEvent in runtime.RunTurnAsync("s1", "read both", TestContext.Current.CancellationToken))
        {
            events.Add(agentEvent);
            if (agentEvent is AgentToolCallCompleted { CallId: "c2" })
            {
                fastDone.TrySetResult();
            }
        }

        events.OfType<AgentToolCallStarted>().Select(e => e.CallId).Should().Equal("c1", "c2");
        events.OfType<AgentToolCallCompleted>().Select(e => e.CallId).Should().Equal("c2", "c1");
        chat.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .Select(r => $"{r.CallId}={r.Result}").Should().Equal("c1=read slow", "c2=read fast");
    }

    [Theory]
    [InlineData(ToolRisk.Write)]
    [InlineData(ToolRisk.Execute)]
    [InlineData(ToolRisk.External)]
    [InlineData(ToolRisk.Delegate)]
    public async Task RunTurnAsync_CallsThatChangeSomething_RunOneAfterAnother(ToolRisk risk)
    {
        var probe = new ConcurrencyProbe(expected: 2);
        var tool = Tool(AIFunctionFactory.Create(probe.RunAsync, "act"), risk);
        var permissions = new ToolPermissionService(Path.Combine(_directory, "permissions.json"), ApprovalMode.FullAuto);
        var chat = new ScriptedChatClient(
            [Call("c1", "act", ("name", "a")), Call("c2", "act", ("name", "b"))],
            [new TextContent("Done.")]);
        var runtime = CreateRuntime(chat, [tool], permissions);

        await CollectAsync(runtime, "s1", "do both");

        probe.MaxRunning.Should().Be(1);
        probe.Log.Should().Equal("start a", "end a", "start b", "end b");
    }

    [Fact]
    public async Task RunTurnAsync_WriteBetweenReads_WaitsForEarlierReadsAndFinishesBeforeLaterOnes()
    {
        var probe = new ConcurrencyProbe(expected: 2, waitMilliseconds: 5000);
        var read = Tool(AIFunctionFactory.Create(probe.RunAsync, "files_read"), ToolRisk.Read);
        var write = Tool(AIFunctionFactory.Create(probe.RunAsync, "files_write"), ToolRisk.Write);
        var permissions = new ToolPermissionService(Path.Combine(_directory, "permissions.json"), ApprovalMode.FullAuto);
        var chat = new ScriptedChatClient(
            [
                Call("c1", "files_read", ("name", "a")),
                Call("c2", "files_read", ("name", "b")),
                Call("c3", "files_write", ("name", "w")),
                Call("c4", "files_read", ("name", "c"))
            ],
            [new TextContent("Done.")]);
        var runtime = CreateRuntime(chat, [read, write], permissions);

        await CollectAsync(runtime, "s1", "read, write, read");

        probe.MaxRunning.Should().Be(2, "the two reads before the write run together");
        probe.Log.Take(4).Should().BeEquivalentTo("start a", "start b", "end a", "end b");
        probe.Log.Skip(4).Should().Equal("start w", "end w", "start c", "end c");
        chat.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .Select(r => r.CallId).Should().Equal("c1", "c2", "c3", "c4");
    }

    [Fact]
    public async Task RunTurnAsync_DeniedCallAmongReads_IsBlockedOnItsOwnWhileTheOthersRun()
    {
        var probe = new ConcurrencyProbe(expected: 2, waitMilliseconds: 5000);
        var secretRuns = 0;
        var read = Tool(AIFunctionFactory.Create(probe.RunAsync, "files_read"), ToolRisk.Read);
        var secret = Tool(AIFunctionFactory.Create(() => { secretRuns++; return "secret"; }, "secrets_read"), ToolRisk.Read);
        var permissions = new ToolPermissionService(Path.Combine(_directory, "permissions.json"));
        permissions.AddRule("secrets_read", allow: false);
        var chat = new ScriptedChatClient(
            [Call("c1", "files_read", ("name", "a")), Call("c2", "secrets_read"), Call("c3", "files_read", ("name", "b"))],
            [new TextContent("Done.")]);
        var runtime = CreateRuntime(chat, [read, secret], permissions);

        var events = await CollectAsync(runtime, "s1", "read all");

        secretRuns.Should().Be(0);
        probe.MaxRunning.Should().Be(2);
        events.OfType<AgentToolCallCompleted>().Should().ContainSingle(e => e.CallId == "c2" && !e.Success && e.Summary == "Blocked by a deny rule");
        chat.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .Select(r => $"{r.CallId}={r.Result!.ToString()!.Split(':')[0]}").Should().Equal("c1=read a", "c2=Blocked", "c3=read b");
    }

    [Fact]
    public async Task RunTurnAsync_PolicyAsksForReads_AsksOneCallAtATime()
    {
        var tool = Tool(AIFunctionFactory.Create((string name) => "read " + name, "files_read"), ToolRisk.Read);
        var chat = new ScriptedChatClient(
            [Call("c1", "files_read", ("name", "a")), Call("c2", "files_read", ("name", "b"))],
            [new TextContent("Done.")]);
        var runtime = CreateRuntime(chat, [tool], new AskForEverything());

        var events = new List<AgentEvent>();
        await using var turn = runtime.RunTurnAsync("s1", "read both", TestContext.Current.CancellationToken).GetAsyncEnumerator();
        while (await turn.MoveNextAsync())
        {
            events.Add(turn.Current);
            if (turn.Current is AgentApprovalRequested)
            {
                break;
            }
        }

        var next = turn.MoveNextAsync().AsTask();
        (await Task.WhenAny(next, Task.Delay(200, TestContext.Current.CancellationToken)))
            .Should().NotBeSameAs(next, "the second call is not announced while the first waits for the user");
        events.OfType<AgentToolCallStarted>().Select(e => e.CallId).Should().Equal("c1");

        runtime.ResolveApproval(events.OfType<AgentApprovalRequested>().Single().RequestId, approved: true).Should().BeTrue();
        while (await next)
        {
            events.Add(turn.Current);
            if (turn.Current is AgentApprovalRequested { CallId: "c2" } second)
            {
                runtime.ResolveApproval(second.RequestId, approved: false);
            }

            next = turn.MoveNextAsync().AsTask();
        }

        events.OfType<AgentApprovalRequested>().Select(e => e.CallId).Should().Equal("c1", "c2");
        chat.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .Select(r => r.CallId).Should().Equal("c1", "c2");
        events.OfType<AgentToolCallCompleted>().Should().Contain(e => e.CallId == "c1" && e.Success)
            .And.Contain(e => e.CallId == "c2" && e.Summary == "Declined");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public async Task RunTurnAsync_MaxParallelToolCalls_CapsHowManyReadsRunAtOnce(int cap, int expectedRunning)
    {
        var probe = new ConcurrencyProbe(expected: 4, waitMilliseconds: 1000);
        var tool = Tool(AIFunctionFactory.Create(probe.RunAsync, "files_read"), ToolRisk.Read);
        var chat = new ScriptedChatClient(
            [.. Enumerable.Range(1, 4).Select(i => Call($"c{i}", "files_read", ("name", $"f{i}")))],
            [new TextContent("Done.")]);
        var runtime = CreateRuntime(chat, [tool], maxParallelToolCalls: cap);

        var events = await CollectAsync(runtime, "s1", "read four");

        probe.MaxRunning.Should().Be(expectedRunning);
        events.Last().Should().BeOfType<AgentTurnCompleted>().Which.ToolCallCount.Should().Be(4);
    }

    [Fact]
    public async Task RunTurnAsync_StoppedWhileReadsRunTogether_StopsThemAllAndKeepsTheThreadValid()
    {
        var store = new JsonAgentSessionStore(Path.Combine(_directory, "sessions"));
        var bothRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var stopped = 0;
        var tool = Tool(
            AIFunctionFactory.Create(
                async (string name, CancellationToken cancellationToken) =>
                {
                    if (Interlocked.Increment(ref started) == 2)
                    {
                        bothRunning.TrySetResult();
                    }

                    try
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken);
                    }
                    finally
                    {
                        Interlocked.Increment(ref stopped);
                    }

                    return "unreachable";
                },
                "files_read"),
            ToolRisk.Read);
        var chat = new ScriptedChatClient(
            [Call("c1", "files_read", ("name", "a")), Call("c2", "files_read", ("name", "b"))],
            [new TextContent("Fine.")]);
        var runtime = CreateRuntime(chat, [tool], store: store);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _ = bothRunning.Task.ContinueWith(_ => cancellation.Cancel(), TaskScheduler.Default);

        var events = new List<AgentEvent>();
        await foreach (var agentEvent in runtime.RunTurnAsync("s1", "read both", cancellation.Token))
        {
            events.Add(agentEvent);
        }

        events.Last().Should().BeOfType<AgentTurnFailed>().Which.Message.Should().Be("Stopped.");
        stopped.Should().Be(2, "no tool call outlives the turn");

        await CollectAsync(runtime, "s1", "never mind");

        var saved = await store.LoadAsync("s1", TestContext.Current.CancellationToken);
        saved.Select(m => m.Role).Should().Equal(ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.User, ChatRole.Assistant);
        saved[2].Contents.OfType<FunctionResultContent>().Select(r => r.CallId).Should().Equal("c1", "c2");
    }

    private static List<ChatMessage> ThreeLongTurns() =>
    [
        new(ChatRole.User, "first"),
        new(ChatRole.Assistant, new string('a', 11000)),
        new(ChatRole.User, "second"),
        new(ChatRole.Assistant, new string('b', 11000)),
        new(ChatRole.User, "third"),
        new(ChatRole.Assistant, new string('c', 11000))
    ];

    private AgentRuntime CreateRuntime(
        IChatClient chat,
        IReadOnlyList<AgentToolDescriptor> tools,
        IToolPermissionService? permissions = null,
        IAgentSessionStore? store = null,
        int maxIterations = 24,
        int maxResultCharacters = 16000,
        int contextTokenBudget = 64000,
        IReadOnlyList<IAgentPromptContributor>? contributors = null,
        string? workspaceRoot = null,
        Func<string?, IChatClient>? chatFactory = null,
        int maxModelRetries = 3,
        int retryDelayMilliseconds = 5,
        int maxParallelToolCalls = 8,
        IOptionsMonitor<AgentRuntimeOptions>? options = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<IAgentToolProvider>(_ => new StaticToolProvider(tools));
        foreach (var contributor in contributors ?? [])
        {
            services.AddScoped(_ => contributor);
        }
        var provider = services.BuildServiceProvider();

        return new AgentRuntime(
            chatFactory ?? (_ => chat),
            provider.GetRequiredService<IServiceScopeFactory>(),
            permissions ?? new ToolPermissionService(Path.Combine(_directory, "permissions.json")),
            store ?? new JsonAgentSessionStore(Path.Combine(_directory, "sessions")),
            options ?? new StaticOptionsMonitor<AgentRuntimeOptions>(new AgentRuntimeOptions
            {
                MaxIterations = maxIterations,
                MaxToolResultCharacters = maxResultCharacters,
                ContextTokenBudget = contextTokenBudget,
                MaxModelRetries = maxModelRetries,
                ModelRetryBaseDelayMilliseconds = retryDelayMilliseconds,
                MaxParallelToolCalls = maxParallelToolCalls
            }),
            NullLogger<AgentRuntime>.Instance,
            () => new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero),
            () => workspaceRoot);
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

    private static AIContent Fail(Exception exception) => new FailContent(exception);

    /// <summary>Makes the scripted model stream throw at this point of a response.</summary>
    private sealed class FailContent(Exception exception) : AIContent
    {
        public Exception Exception { get; } = exception;
    }

    /// <summary>
    /// A tool body that records how many calls run at once. Each call waits until the expected number
    /// run together, or gives up after a short wait when the runtime runs them one after another.
    /// </summary>
    private sealed class ConcurrencyProbe(int expected, int waitMilliseconds = 300)
    {
        private readonly TaskCompletionSource _together = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _gate = new();
        private int _running;

        public int MaxRunning { get; private set; }

        public List<string> Log { get; } = [];

        public async Task<string> RunAsync(string name, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _running++;
                MaxRunning = Math.Max(MaxRunning, _running);
                Log.Add($"start {name}");
                if (_running >= expected)
                {
                    _together.TrySetResult();
                }
            }

            await Task.WhenAny(_together.Task, Task.Delay(waitMilliseconds, cancellationToken));

            lock (_gate)
            {
                _running--;
                Log.Add($"end {name}");
            }

            return "read " + name;
        }
    }

    private sealed class AskForEverything : IToolPermissionService
    {
        public ApprovalMode Mode { get; set; } = ApprovalMode.Ask;

        public IReadOnlyCollection<string> AllowRules => [];

        public IReadOnlyCollection<string> DenyRules => [];

        public ToolPermissionDecision Evaluate(AgentToolDescriptor tool, string argumentsJson) => ToolPermissionDecision.Ask;

        public string SuggestAllowRule(AgentToolDescriptor tool, string argumentsJson) => tool.Name;

        public void AddRule(string rule, bool allow)
        {
        }

        public void RemoveRule(string rule)
        {
        }
    }

    private sealed class FixedPromptContributor(string? section = null, bool throws = false) : IAgentPromptContributor
    {
        public string? GetPromptSection() => throws ? throw new InvalidOperationException("broken") : section;
    }

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
                if (content is FailContent fail)
                {
                    throw fail.Exception;
                }

                yield return new ChatResponseUpdate(ChatRole.Assistant, [content]) { MessageId = $"m{Requests.Count}" };
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}

internal sealed class MutableOptionsMonitor(AgentRuntimeOptions value) : IOptionsMonitor<AgentRuntimeOptions>
{
    public AgentRuntimeOptions CurrentValue { get; set; } = value;

    public AgentRuntimeOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<AgentRuntimeOptions, string?> listener) => null;
}
