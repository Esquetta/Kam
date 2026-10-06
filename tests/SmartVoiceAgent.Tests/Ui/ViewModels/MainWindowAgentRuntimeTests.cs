using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class MainWindowAgentRuntimeTests
{
    [Fact]
    public async Task SubmitCommandInputAsync_WithAgentRuntime_StreamsReplyAndToolStepsIntoThread()
    {
        var runtime = new ScriptedRuntime();
        var commandInput = new RecordingCommandInput();
        var viewModel = CreateViewModel(runtime);
        viewModel.SetCommandInputService(commandInput);
        var session = viewModel.SelectedAgentChatSession!;
        viewModel.CommandInputText = "list my downloads";
        var sid = session.SessionId;
        runtime.Script(
            new AgentTextDelta(sid, "Let me "),
            new AgentTextDelta(sid, "look."),
            new AgentToolCallStarted(sid, "c1", "files_list", "List Files", "{\"path\":\"Downloads\"}", ToolRisk.Read),
            new AgentToolCallCompleted(sid, "c1", "files_list", true, "Found 3 files"),
            new AgentTextDelta(sid, "You have 3 files."),
            new AgentTurnCompleted(sid, "You have 3 files.", 1, 10, 5));
        runtime.Finish();

        await viewModel.SubmitCommandInputAsync();

        runtime.Messages.Should().Equal("list my downloads");
        runtime.SessionIds.Should().Equal(sid);
        commandInput.Submitted.Should().BeEmpty("chat goes to the agent runtime, not the legacy planner");
        viewModel.CommandInputText.Should().BeEmpty();
        session.Messages.Select(m => m.IsToolStep ? $"tool:{m.ToolName}:{m.ToolStatusText}" : $"{m.Role}:{m.Content}")
            .Should().Equal(
                "You:list my downloads",
                "Kam:Let me look.",
                "tool:files_list:Done",
                "Kam:You have 3 files.");
        session.Messages[2].ArgumentsPreview.Should().Be("path: Downloads");
        session.Messages[2].ToolSummary.Should().Be("Found 3 files");
        session.Messages.Should().OnlyContain(m => !m.IsStreaming);
        session.Summary.Should().Be("You have 3 files.");
        viewModel.IsAgentTurnRunning.Should().BeFalse();
        session.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ApproveToolCallCommand_AnswersPendingRequestAndUpdatesCard()
    {
        var runtime = new ScriptedRuntime();
        var viewModel = CreateViewModel(runtime);
        var session = viewModel.SelectedAgentChatSession!;
        var sid = session.SessionId;
        runtime.Script(
            new AgentToolCallStarted(sid, "c1", "shell_run", "Run Shell Command", "{\"command\":\"git status\"}", ToolRisk.Execute),
            new AgentApprovalRequested(sid, "req-1", "c1", "shell_run", "Run Shell Command", "{}", ToolRisk.Execute));
        viewModel.CommandInputText = "git status";

        var turn = viewModel.SubmitCommandInputAsync();
        var step = await WaitForAsync(() => session.Messages.FirstOrDefault(m => m.IsAwaitingApproval));

        viewModel.IsAgentTurnRunning.Should().BeTrue();
        viewModel.IsSendVisible.Should().BeFalse();
        step.ToolStatusText.Should().Be("Needs approval");
        viewModel.AlwaysAllowToolCallCommand.Execute(step);

        runtime.Resolutions.Should().Equal(("req-1", true, true));
        step.IsAwaitingApproval.Should().BeFalse();
        step.ToolStatusText.Should().Be("Running");

        runtime.Script(
            new AgentToolCallCompleted(sid, "c1", "shell_run", true, "On branch main"),
            new AgentTurnCompleted(sid, "Clean tree.", 1, 0, 0));
        runtime.Finish();
        await turn;

        step.ToolStatusText.Should().Be("Done");
        session.Messages.Last().Content.Should().Be("Clean tree.");
    }

    [Fact]
    public async Task DenyToolCallCommand_MarksStepDeclined()
    {
        var runtime = new ScriptedRuntime();
        var viewModel = CreateViewModel(runtime);
        var session = viewModel.SelectedAgentChatSession!;
        var sid = session.SessionId;
        runtime.Script(
            new AgentToolCallStarted(sid, "c1", "processes_kill", "Kill Process", "{}", ToolRisk.Execute),
            new AgentApprovalRequested(sid, "req-9", "c1", "processes_kill", "Kill Process", "{}", ToolRisk.Execute));
        viewModel.CommandInputText = "kill it";

        var turn = viewModel.SubmitCommandInputAsync();
        var step = await WaitForAsync(() => session.Messages.FirstOrDefault(m => m.IsAwaitingApproval));
        viewModel.DenyToolCallCommand.Execute(step);
        runtime.Finish();
        await turn;

        runtime.Resolutions.Should().Equal(("req-9", false, false));
        step.ToolStatusText.Should().Be("Declined");
        step.IsToolFailed.Should().BeTrue();
    }

    [Fact]
    public async Task StopAgentTurnCommand_CancelsTurnAndAbandonsOpenSteps()
    {
        var runtime = new ScriptedRuntime();
        var viewModel = CreateViewModel(runtime);
        var session = viewModel.SelectedAgentChatSession!;
        var sid = session.SessionId;
        runtime.Script(new AgentToolCallStarted(sid, "c1", "web_search", "Search Web", "{}", ToolRisk.Network));
        viewModel.CommandInputText = "search";

        var turn = viewModel.SubmitCommandInputAsync();
        var step = await WaitForAsync(() => session.Messages.FirstOrDefault(m => m.IsToolStep));
        viewModel.StopAgentTurnCommand.Execute(null);
        await turn;

        runtime.WasCancelled.Should().BeTrue();
        step.ToolStatusText.Should().Be("Failed");
        step.ToolSummary.Should().Be("Stopped before it finished.");
        viewModel.IsAgentTurnRunning.Should().BeFalse();
    }

    [Fact]
    public async Task TurnFailure_ShowsErrorMessage()
    {
        var runtime = new ScriptedRuntime();
        var viewModel = CreateViewModel(runtime);
        var session = viewModel.SelectedAgentChatSession!;
        runtime.Script(new AgentTurnFailed(session.SessionId, "401 Unauthorized"));
        runtime.Finish();
        viewModel.CommandInputText = "hi";

        await viewModel.SubmitCommandInputAsync();

        session.Messages.Last().Role.Should().Be("System");
        session.Messages.Last().Content.Should().Be("Error: 401 Unauthorized");
    }

    [Fact]
    public void SelectedApprovalMode_UpdatesPermissionService()
    {
        var permissions = new FakePermissions { Mode = ApprovalMode.AutoEdit };
        var viewModel = new MainWindowViewModel();
        viewModel.SetAgentRuntime(new ScriptedRuntime(), permissions, new EmptySessionStore());

        viewModel.SelectedApprovalMode.Mode.Should().Be(ApprovalMode.AutoEdit);
        viewModel.IsAgentRuntimeEnabled.Should().BeTrue();

        viewModel.SelectedApprovalMode = MainWindowViewModel.ApprovalModes.Single(m => m.Mode == ApprovalMode.FullAuto);

        permissions.Mode.Should().Be(ApprovalMode.FullAuto);
    }

    [Fact]
    public async Task SetAgentRuntime_ListsSavedThreadsAndLoadsHistoryWhenOpened()
    {
        var store = new EmptySessionStore();
        store.Sessions["saved-1"] =
        [
            new ChatMessage(ChatRole.User, "open notepad"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "apps_open", new Dictionary<string, object?> { ["applicationName"] = "notepad" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c1", "Opened Notepad.")]),
            new ChatMessage(ChatRole.Assistant, "Notepad is open.")
        ];
        var viewModel = new MainWindowViewModel();

        viewModel.SetAgentRuntime(new ScriptedRuntime(), new FakePermissions(), store);
        var saved = await WaitForAsync(() => viewModel.AgentChatSessions.FirstOrDefault(s => s.SessionId == "saved-1"));

        saved.Title.Should().Be("open notepad");
        saved.MessageCountText.Should().Be("4 messages");
        saved.NeedsHistoryLoad.Should().BeTrue();

        viewModel.SelectAgentChatCommand.Execute(saved);
        await WaitForAsync(() => saved.NeedsHistoryLoad ? null : saved);

        saved.Messages.Select(m => m.IsToolStep ? $"tool:{m.ToolName}:{m.ToolStatusText}:{m.ArgumentsPreview}" : $"{m.Role}:{m.Content}")
            .Should().Equal(
                "You:open notepad",
                "tool:apps_open:Done:applicationName: notepad",
                "Kam:Notepad is open.");
    }

    [Fact]
    public void BuildTimeline_MarksDeclinedAndFailedCalls()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, "do things"),
            new(ChatRole.Assistant, [new TextContent("Trying."), new FunctionCallContent("a", "shell_run"), new FunctionCallContent("b", "files_read")]),
            new(ChatRole.Tool, [
                new FunctionResultContent("a", "The user declined this tool call. Do not retry it."),
                new FunctionResultContent("b", "Error (Failed): missing file")
            ])
        ];

        var timeline = MainWindowViewModel.BuildTimeline(history);

        timeline.Select(m => m.IsToolStep ? $"{m.ToolName}:{m.ToolStatusText}" : m.Content)
            .Should().Equal("do things", "Trying.", "shell_run:Declined", "files_read:Failed");
        timeline[3].ToolSummary.Should().Be("Error (Failed): missing file");
    }

    [Fact]
    public async Task RunAgentTurnAsync_ContextCompacted_ShowsNoticeInThread()
    {
        var runtime = new ScriptedRuntime();
        var viewModel = CreateViewModel(runtime);
        var session = viewModel.SelectedAgentChatSession!;
        var sid = session.SessionId;
        runtime.Script(
            new AgentContextCompacted(sid, 12, 70000, 9000),
            new AgentTextDelta(sid, "Continuing."),
            new AgentTurnCompleted(sid, "Continuing.", 0, 0, 0));
        runtime.Finish();

        await viewModel.RunAgentTurnAsync(session, "keep going");

        session.Messages.Select(m => $"{m.Role}:{m.Content}").Should().Equal(
            "System:Earlier messages were summarized to save context.",
            "Kam:Continuing.");
    }

    [Fact]
    public async Task SubmitCommandInputAsync_CompactCommand_GoesToAgentRuntime()
    {
        var runtime = new ScriptedRuntime();
        var viewModel = CreateViewModel(runtime);
        var session = viewModel.SelectedAgentChatSession!;
        runtime.Script(new AgentTurnCompleted(session.SessionId, "There is nothing to compact yet.", 0, 0, 0));
        runtime.Finish();
        viewModel.CommandInputText = "/compact";

        await viewModel.SubmitCommandInputAsync();

        runtime.Messages.Should().Equal("/compact");
        session.Messages.Select(m => $"{m.Role}:{m.Content}").Should().Equal(
            "You:/compact",
            "Kam:There is nothing to compact yet.");
    }

    [Fact]
    public void BuildTimeline_ShowsWhereEarlierMessagesWereSummarized()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, "first"),
            new(ChatRole.Assistant, "hello"),
            new(ChatRole.System, "Summary of the earlier conversation (older messages were compacted to save context):\nfirst"),
            new(ChatRole.User, "next")
        ];

        var timeline = MainWindowViewModel.BuildTimeline(history);

        timeline.Select(m => $"{m.Role}:{m.Content}").Should().Equal(
            "You:first",
            "Kam:hello",
            "System:Earlier messages were summarized to save context.",
            "You:next");
    }

    [Theory]
    [InlineData(0, "now")]
    [InlineData(5, "5m")]
    [InlineData(180, "3h")]
    public void FormatRelativeTime_UsesShortLabels(int minutesAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 10, 5, 18, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 5, 18, 0, 0)));

        MainWindowViewModel.FormatRelativeTime(now.AddMinutes(-minutesAgo), now).Should().Be(expected);
    }

    [Fact]
    public void FormatRelativeTime_OlderThanYesterday_ShowsDate()
    {
        var now = new DateTimeOffset(2026, 10, 5, 18, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 5, 18, 0, 0)));

        MainWindowViewModel.FormatRelativeTime(now.AddDays(-1), now).Should().Be("Yesterday");
        MainWindowViewModel.FormatRelativeTime(now.AddDays(-3), now).Should().Be("Oct 2");
    }

    private static MainWindowViewModel CreateViewModel(ScriptedRuntime runtime)
    {
        var viewModel = new MainWindowViewModel();
        viewModel.SetAgentRuntime(runtime, new FakePermissions(), new EmptySessionStore());
        return viewModel;
    }

    private static async Task<T> WaitForAsync<T>(Func<T?> probe) where T : class
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (probe() is { } value)
            {
                return value;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not met.");
    }

    private sealed class ScriptedRuntime : IAgentRuntime
    {
        private readonly Channel<AgentEvent> _events = Channel.CreateUnbounded<AgentEvent>();
        private bool _autoFinish = true;

        public List<string> Messages { get; } = [];

        public List<string> SessionIds { get; } = [];

        public List<(string RequestId, bool Approved, bool AlwaysAllow)> Resolutions { get; } = [];

        public bool WasCancelled { get; private set; }

        public void Script(params AgentEvent[] events)
        {
            foreach (var agentEvent in events)
            {
                _events.Writer.TryWrite(agentEvent);
                if (agentEvent is AgentApprovalRequested or AgentToolCallStarted)
                {
                    _autoFinish = false;
                }
            }
        }

        public void Finish() => _events.Writer.TryComplete();

        public async IAsyncEnumerable<AgentEvent> RunTurnAsync(
            string sessionId,
            string userMessage,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            SessionIds.Add(sessionId);
            Messages.Add(userMessage);
            if (_autoFinish)
            {
                Finish();
            }

            while (true)
            {
                AgentEvent next;
                try
                {
                    if (!await _events.Reader.WaitToReadAsync(cancellationToken) || !_events.Reader.TryRead(out next!))
                    {
                        yield break;
                    }
                }
                catch (OperationCanceledException)
                {
                    WasCancelled = true;
                    yield break;
                }

                yield return next;
            }
        }

        public bool ResolveApproval(string requestId, bool approved, bool alwaysAllow = false)
        {
            Resolutions.Add((requestId, approved, alwaysAllow));
            return true;
        }
    }

    private sealed class FakePermissions : IToolPermissionService
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

    private sealed class EmptySessionStore : IAgentSessionStore
    {
        public Dictionary<string, IReadOnlyList<ChatMessage>> Sessions { get; } = [];

        public Task<IReadOnlyList<ChatMessage>> LoadAsync(string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Sessions.TryGetValue(sessionId, out var messages) ? messages : (IReadOnlyList<ChatMessage>)[]);

        public Task SaveAsync(string sessionId, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<AgentSessionSummary>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentSessionSummary>>(Sessions
                .Select(pair => new AgentSessionSummary(
                    pair.Key,
                    pair.Value.FirstOrDefault(m => m.Role == ChatRole.User)?.Text ?? "New chat",
                    DateTimeOffset.Now.AddHours(-2),
                    pair.Value.Count,
                    null))
                .ToList());

        public Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingCommandInput : ICommandInputService
    {
        public event EventHandler<CommandResultEventArgs>? OnResult;

        public List<string> Submitted { get; } = [];

        public void SubmitCommand(string command) => Submitted.Add(command);

        public Task<string> ReadCommandAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public void PublishResult(string command, string result, bool success = true)
        {
            OnResult?.Invoke(this, new CommandResultEventArgs { Command = command, Result = result, Success = success });
        }
    }
}
