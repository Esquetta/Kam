using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Tests.Ui.Services;
using SmartVoiceAgent.Ui.Services;
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

    [Fact]
    public async Task SubmitCommandInputAsync_MarkdownCommand_SendsExpandedPromptAsAgentTurn()
    {
        var runtime = new ScriptedRuntime();
        var viewModel = new MainWindowViewModel();
        viewModel.SetAgentRuntime(runtime, new FakePermissions(), new EmptySessionStore(), new FakeCommands());
        var session = viewModel.SelectedAgentChatSession!;
        runtime.Script(new AgentTurnCompleted(session.SessionId, "Looks good.", 0, 0, 0));
        runtime.Finish();
        viewModel.CommandInputText = "/review src/App.cs";

        await viewModel.SubmitCommandInputAsync();

        runtime.Messages.Should().Equal("Review src/App.cs for bugs.");
        session.Messages.First().Content.Should().Be("/review src/App.cs", "the thread shows what was typed");
    }

    [Fact]
    public async Task SubmitVoiceCommandAsync_RunsTurnInSelectedChat()
    {
        var runtime = new ScriptedRuntime();
        var commandInput = new RecordingCommandInput();
        var viewModel = CreateViewModel(runtime);
        viewModel.SetCommandInputService(commandInput);
        var session = viewModel.SelectedAgentChatSession!;
        runtime.Script(new AgentTurnCompleted(session.SessionId, "Opened Spotify.", 1, 0, 0));
        runtime.Finish();

        await viewModel.SubmitVoiceCommandAsync("open spotify");

        runtime.Messages.Should().Equal("open spotify");
        runtime.SessionIds.Should().Equal(session.SessionId);
        commandInput.Submitted.Should().BeEmpty();
        session.Messages.Select(m => $"{m.Role}:{m.Content}").Should().Equal("You:open spotify", "Kam:Opened Spotify.");
    }

    [Fact]
    public async Task SubmitVoiceCommandAsync_ReadsTheReplyAloud()
    {
        var settingsDirectory = Path.Combine(Path.GetTempPath(), "kam-voice-reply-tests", Guid.NewGuid().ToString("N"));
        using var settings = new JsonSettingsService(settingsDirectory);
        var runtime = new ScriptedRuntime();
        var viewModel = new MainWindowViewModel(settings);
        viewModel.SetAgentRuntime(runtime, new FakePermissions(), new EmptySessionStore());
        var fakes = new VoiceFakes();
        viewModel.SetVoiceAssistant(fakes.CreateAssistant());
        var session = viewModel.SelectedAgentChatSession!;
        runtime.Script(new AgentTurnCompleted(session.SessionId, "**Opened** Spotify.", 1, 0, 0));
        runtime.Finish();

        await viewModel.SubmitVoiceCommandAsync("open spotify");

        await VoiceFakes.WaitUntilAsync(() => fakes.Speech.Spoken.Count == 1, "the reply is read aloud");
        fakes.Speech.Spoken[0].Should().Be("Opened Spotify.");
    }

    [Theory]
    [InlineData("Voice", false)]
    [InlineData("All", true)]
    public async Task TypedTurn_IsReadAloudOnlyWhenEveryReplyIs(string spokenReplies, bool expected)
    {
        var settingsDirectory = Path.Combine(Path.GetTempPath(), "kam-voice-reply-tests", Guid.NewGuid().ToString("N"));
        using var settings = new JsonSettingsService(settingsDirectory);
        settings.SpokenReplies = spokenReplies;
        var runtime = new ScriptedRuntime();
        var viewModel = new MainWindowViewModel(settings);
        viewModel.SetAgentRuntime(runtime, new FakePermissions(), new EmptySessionStore());
        var fakes = new VoiceFakes();
        viewModel.SetVoiceAssistant(fakes.CreateAssistant());
        var session = viewModel.SelectedAgentChatSession!;
        runtime.Script(new AgentTurnCompleted(session.SessionId, "Done.", 0, 0, 0));
        runtime.Finish();

        await viewModel.RunAgentTurnAsync(session, "tidy my desktop");

        if (expected)
        {
            await VoiceFakes.WaitUntilAsync(() => fakes.Speech.Spoken.Count == 1, "the reply is read aloud");
        }
        else
        {
            await Task.Delay(100);
            fakes.Speech.Spoken.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task SubmitVoiceCommandAsync_WhileTurnRunning_QueuesCommandUntilTurnEnds()
    {
        var runtime = new ScriptedRuntime();
        var viewModel = CreateViewModel(runtime);
        var session = viewModel.SelectedAgentChatSession!;
        var turn = StartBlockedTurnAsync(viewModel, runtime, session, "build it");
        await WaitForAsync(() => session.Messages.FirstOrDefault(m => m.IsAwaitingApproval));

        await viewModel.SubmitVoiceCommandAsync("open spotify");

        runtime.Messages.Should().Equal("build it");
        viewModel.QueuedAgentMessagesText.Should().Be("Sends next: open spotify");
        session.Messages.Should().NotContain(m => m.Content == "open spotify", "the request shows up when it runs");

        runtime.Finish();
        await turn;

        runtime.Messages.Should().Equal("build it", "open spotify");
        viewModel.HasQueuedAgentMessages.Should().BeFalse();
        session.Messages.Where(m => !m.IsToolStep).Select(m => $"{m.Role}:{m.Content}")
            .Should().Equal("You:build it", "You:open spotify");
    }

    [Fact]
    public async Task SubmitCommandInputAsync_WhileTurnRunning_SendsMessagesInOrderAfterTheTurn()
    {
        var runtime = new ScriptedRuntime();
        var viewModel = CreateViewModel(runtime);
        var session = viewModel.SelectedAgentChatSession!;
        var turn = StartBlockedTurnAsync(viewModel, runtime, session, "build it");
        await WaitForAsync(() => session.Messages.FirstOrDefault(m => m.IsToolStep));

        viewModel.CommandInputText = "then run the tests";
        await viewModel.SubmitCommandInputAsync();
        viewModel.CommandInputText = "and commit";
        await viewModel.SubmitCommandInputAsync();

        viewModel.CommandInputText.Should().BeEmpty();
        viewModel.QueuedAgentMessages.Select(m => m.DisplayText).Should().Equal("then run the tests", "and commit");
        viewModel.QueuedAgentMessagesText.Should().Be("Sends next: then run the tests (+1 more)");

        runtime.Finish();
        await turn;

        runtime.Messages.Should().Equal("build it", "then run the tests", "and commit");
        runtime.SessionIds.Should().AllBe(session.SessionId);
        viewModel.HasQueuedAgentMessages.Should().BeFalse();
        viewModel.IsAgentTurnRunning.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitCommand_PressedWhileTurnRuns_QueuesTheMessage()
    {
        var runtime = new ScriptedRuntime();
        var viewModel = CreateViewModel(runtime);
        var session = viewModel.SelectedAgentChatSession!;
        runtime.Script(new AgentToolCallStarted(session.SessionId, "c1", "shell_run", "Run Shell Command", "{}", ToolRisk.Execute));
        viewModel.CommandInputText = "build it";
        viewModel.SubmitCommand.Execute(null);
        await WaitForAsync(() => session.Messages.FirstOrDefault(m => m.IsToolStep));

        // Enter in the composer runs the command again while the first turn is still going.
        viewModel.CommandInputText = "then run the tests";
        viewModel.SubmitCommand.Execute(null);

        await WaitForAsync(() => viewModel.HasQueuedAgentMessages ? viewModel : null);
        runtime.Finish();
        await WaitForAsync(() => runtime.Messages.Count == 2 && !viewModel.IsAgentTurnRunning ? runtime : null);

        runtime.Messages.Should().Equal("build it", "then run the tests");
    }

    [Fact]
    public async Task ClearQueuedAgentMessagesCommand_DropsQueuedMessages()
    {
        var runtime = new ScriptedRuntime();
        var viewModel = CreateViewModel(runtime);
        var session = viewModel.SelectedAgentChatSession!;
        var turn = StartBlockedTurnAsync(viewModel, runtime, session, "build it");
        await WaitForAsync(() => session.Messages.FirstOrDefault(m => m.IsToolStep));
        viewModel.CommandInputText = "then run the tests";
        await viewModel.SubmitCommandInputAsync();

        viewModel.ClearQueuedAgentMessagesCommand.Execute(null);
        runtime.Finish();
        await turn;

        viewModel.HasQueuedAgentMessages.Should().BeFalse();
        runtime.Messages.Should().Equal("build it");
    }

    [Fact]
    public void RouteVoiceCommand_WithoutAgentRuntime_LeavesCommandToLegacyPlanner()
    {
        new MainWindowViewModel().RouteVoiceCommand("open spotify").Should().BeFalse();
    }

    [Fact]
    public void StartNewTask_OpensNewSelectedChat()
    {
        var viewModel = CreateViewModel(new ScriptedRuntime());
        var previous = viewModel.SelectedAgentChatSession;

        viewModel.StartNewTask();

        viewModel.SelectedAgentChatSession.Should().NotBeSameAs(previous);
        viewModel.SelectedAgentChatSession!.Title.Should().Be("New chat");
        viewModel.AgentChatSessions.First().Should().BeSameAs(viewModel.SelectedAgentChatSession);
    }

    private static Task StartBlockedTurnAsync(
        MainWindowViewModel viewModel,
        ScriptedRuntime runtime,
        AgentChatSessionViewModel session,
        string message)
    {
        runtime.Script(
            new AgentToolCallStarted(session.SessionId, "c1", "shell_run", "Run Shell Command", "{}", ToolRisk.Execute),
            new AgentApprovalRequested(session.SessionId, "req-1", "c1", "shell_run", "Run Shell Command", "{}", ToolRisk.Execute));
        viewModel.CommandInputText = message;
        return viewModel.SubmitCommandInputAsync();
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

        public Task<AgentSessionSummary?> GetSummaryAsync(string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AgentSessionSummary?>(null);

        public Task RenameAsync(string sessionId, string? title, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetModelAsync(string sessionId, string? modelId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeCommands : IAgentCommandCatalog
    {
        public string UserCommandsDirectory => "commands";

        public IReadOnlyList<SmartVoiceAgent.Core.Models.Agents.Extensions.AgentCommandInfo> GetCommands() => [];

        public bool TryExpand(string input, out string prompt)
        {
            prompt = input.StartsWith("/review ", StringComparison.Ordinal)
                ? $"Review {input["/review ".Length..]} for bugs."
                : string.Empty;
            return prompt.Length > 0;
        }
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
