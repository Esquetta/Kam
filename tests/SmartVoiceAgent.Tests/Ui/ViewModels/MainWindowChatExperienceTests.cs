using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.AI;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class MainWindowChatExperienceTests : IDisposable
{
    private readonly string _settingsDirectory = Path.Combine(
        Path.GetTempPath(),
        "kam-chat-experience-tests",
        Guid.NewGuid().ToString("N"));

    private readonly List<JsonSettingsService> _settings = [];

    public void Dispose()
    {
        foreach (var settings in _settings)
        {
            settings.Dispose();
        }

        if (Directory.Exists(_settingsDirectory))
        {
            Directory.Delete(_settingsDirectory, recursive: true);
        }
    }

    [Fact]
    public void Search_MatchesTitlesAndLastMessages_AndClearShowsEveryThread()
    {
        var store = new RecordingStore("Release notes draft", "Plan the installer", "Weekly report");
        var viewModel = CreateViewModel(store);
        viewModel.SelectedAgentChatSession!.Summary = "Deploy finished on staging";

        viewModel.AgentChatSearchText = "RELEASE";
        VisibleTitles(viewModel).Should().Equal("Release notes draft");
        viewModel.IsAgentChatSearchActive.Should().BeTrue();

        viewModel.AgentChatSearchText = "staging";
        VisibleTitles(viewModel).Should().Equal(viewModel.SelectedAgentChatSession.Title);

        viewModel.AgentChatSearchText = "nothing like this";
        viewModel.HasNoAgentChatSearchResults.Should().BeTrue();

        viewModel.ClearAgentChatSearchCommand.Execute(null);
        viewModel.AgentChatSearchText.Should().BeEmpty();
        viewModel.AgentChatSessions.Should().OnlyContain(session => session.IsVisibleInList);
        viewModel.HasNoAgentChatSearchResults.Should().BeFalse();
    }

    [Fact]
    public void NewAgentChatCommand_ClearsTheSearchSoTheNewChatShows()
    {
        var viewModel = CreateViewModel(new RecordingStore("Release notes draft"));
        viewModel.AgentChatSearchText = "release";

        viewModel.NewAgentChatCommand.Execute(null);

        viewModel.AgentChatSearchText.Should().BeEmpty();
        viewModel.SelectedAgentChatSession!.IsVisibleInList.Should().BeTrue();
    }

    [Fact]
    public async Task Rename_SavesTrimmedTitle_AndSendingAMessageKeepsIt()
    {
        var store = new RecordingStore();
        var viewModel = CreateViewModel(store, new TextRuntime("Sure."));
        viewModel.NewAgentChatCommand.Execute(null);
        var session = viewModel.SelectedAgentChatSession!;

        viewModel.RenameAgentChatCommand.Execute(session);
        session.IsRenaming.Should().BeTrue();
        session.EditTitle.Should().Be(session.Title);
        session.EditTitle = "  Plan the\nrelease  ";
        viewModel.CommitAgentChatRename(session);

        session.IsRenaming.Should().BeFalse();
        session.Title.Should().Be("Plan the release");
        session.HasCustomTitle.Should().BeTrue();
        store.Renames.Should().Equal((session.SessionId, "Plan the release"));

        viewModel.CommandInputText = "draft the release notes";
        await viewModel.SubmitCommandInputAsync();

        session.Title.Should().Be("Plan the release", "a renamed chat is not retitled from its first message");
    }

    [Fact]
    public void Rename_BlankOrCancelled_KeepsTheTitle()
    {
        var store = new RecordingStore("Weekly report");
        var viewModel = CreateViewModel(store);
        var session = viewModel.AgentChatSessions.Single(s => s.Title == "Weekly report");

        viewModel.RenameAgentChatCommand.Execute(session);
        session.EditTitle = "   ";
        viewModel.CommitAgentChatRename(session);

        viewModel.RenameAgentChatCommand.Execute(session);
        session.EditTitle = "Something else";
        viewModel.CancelAgentChatRename(session);
        viewModel.CommitAgentChatRename(session);

        session.Title.Should().Be("Weekly report");
        session.IsRenaming.Should().BeFalse();
        store.Renames.Should().BeEmpty();
    }

    [Fact]
    public void Rename_LongTitle_IsCutToTheStoreLimit()
    {
        var viewModel = CreateViewModel(new RecordingStore());
        var session = viewModel.SelectedAgentChatSession!;

        viewModel.RenameAgentChatCommand.Execute(session);
        session.EditTitle = new string('a', 300);
        viewModel.CommitAgentChatRename(session);

        session.Title.Should().HaveLength(SmartVoiceAgent.Infrastructure.Agent.Runtime.JsonAgentSessionStore.MaxTitleLength);
    }

    [Fact]
    public void Rename_ThreadHiddenBySearch_ClearsTheSearch()
    {
        var viewModel = CreateViewModel(new RecordingStore("Weekly report"));
        viewModel.AgentChatSearchText = "weekly";
        viewModel.SelectedAgentChatSession!.IsVisibleInList.Should().BeFalse();

        viewModel.RenameAgentChatCommand.Execute(null);

        viewModel.AgentChatSearchText.Should().BeEmpty();
        viewModel.SelectedAgentChatSession.IsRenaming.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_AsksFirst_ThenRemovesTheThreadAndSelectsTheNextOne()
    {
        var store = new RecordingStore("Release notes draft", "Plan the installer", "Weekly report");
        var viewModel = CreateViewModel(store);
        var target = viewModel.AgentChatSessions.Single(s => s.Title == "Plan the installer");
        viewModel.SelectAgentChatCommand.Execute(target);
        viewModel.RenameAgentChatCommand.Execute(viewModel.AgentChatSessions[0]);

        viewModel.DeleteAgentChatCommand.Execute(target);

        target.IsConfirmingDelete.Should().BeTrue();
        viewModel.AgentChatSessions[0].IsRenaming.Should().BeFalse("only one row edits at a time");
        viewModel.CancelDeleteAgentChatCommand.Execute(target);
        target.IsConfirmingDelete.Should().BeFalse();
        viewModel.AgentChatSessions.Should().Contain(target);

        viewModel.DeleteAgentChatCommand.Execute(target);
        viewModel.ConfirmDeleteAgentChatCommand.Execute(target);
        await WaitForAsync(() => store.Deletes.Count > 0 ? store.Deletes : null);

        store.Deletes.Should().Equal("s2");
        viewModel.AgentChatSessions.Should().NotContain(target);
        viewModel.SelectedAgentChatSession!.Title.Should().Be("Weekly report");
    }

    [Fact]
    public async Task Delete_OnlyThread_StartsANewChat()
    {
        var store = new RecordingStore();
        var viewModel = CreateViewModel(store);
        var only = viewModel.AgentChatSessions.Single();

        viewModel.DeleteAgentChatCommand.Execute(only);
        viewModel.ConfirmDeleteAgentChatCommand.Execute(only);
        await WaitForAsync(() => store.Deletes.Count > 0 ? store.Deletes : null);

        viewModel.AgentChatSessions.Should().ContainSingle().Which.Should().NotBeSameAs(only);
        viewModel.SelectedAgentChatSession!.Title.Should().Be("New chat");
    }

    [Fact]
    public void Delete_RunningThread_IsNotOffered()
    {
        var viewModel = CreateViewModel(new RecordingStore());
        var session = viewModel.SelectedAgentChatSession!;
        session.IsRunning = true;

        viewModel.DeleteAgentChatCommand.Execute(session);

        session.CanDelete.Should().BeFalse();
        session.IsConfirmingDelete.Should().BeFalse();
    }

    [Fact]
    public void ModelPicker_StartsOnTheSettingsModel_AndSavesAChoicePerThread()
    {
        var store = new RecordingStore("Weekly report");
        var viewModel = CreateViewModel(store);
        var first = viewModel.SelectedAgentChatSession!;

        var defaultOption = viewModel.AgentChatModelOptions[0];
        defaultOption.IsDefault.Should().BeTrue();
        defaultOption.Label.Should().Be("vendor/model-a");
        defaultOption.Caption.Should().Be("Settings default");
        viewModel.SelectedAgentChatModel.Should().Be(defaultOption);
        viewModel.AgentChatModelOptions.Should().HaveCountGreaterThan(1);

        var chosen = viewModel.AgentChatModelOptions[1];
        viewModel.SelectedAgentChatModel = chosen;

        first.ModelId.Should().Be(chosen.ModelId);
        store.Models.Should().Equal((first.SessionId, chosen.ModelId));
        viewModel.AgentChatModelTip.Should().Contain(chosen.ModelId!);

        viewModel.SelectAgentChatCommand.Execute(viewModel.AgentChatSessions.Single(s => s.Title == "Weekly report"));
        viewModel.SelectedAgentChatModel!.IsDefault.Should().BeTrue();

        viewModel.SelectAgentChatCommand.Execute(first);
        viewModel.SelectedAgentChatModel!.ModelId.Should().Be(chosen.ModelId);

        viewModel.SelectedAgentChatModel = viewModel.AgentChatModelOptions[0];
        first.ModelId.Should().BeNull();
        store.Models.Last().Should().Be((first.SessionId, (string?)null));
    }

    [Fact]
    public void ModelPicker_ShowsAModelSavedWithTheThread_EvenWhenNotSuggested()
    {
        var store = new RecordingStore();
        store.Saved.Add(new AgentSessionSummary("custom", "Local model chat", DateTimeOffset.Now, 2, null, "lab/custom-model"));
        var viewModel = CreateViewModel(store);

        viewModel.SelectAgentChatCommand.Execute(viewModel.AgentChatSessions.Single(s => s.SessionId == "custom"));

        viewModel.SelectedAgentChatModel!.ModelId.Should().Be("lab/custom-model");
        viewModel.SelectedAgentChatModel.Caption.Should().Be("This chat");
        viewModel.AgentChatModelOptions[1].Should().Be(viewModel.SelectedAgentChatModel);
    }

    [Fact]
    public void OnRuntimeSettingsApplied_ModelChange_RefreshesThePicker()
    {
        var viewModel = CreateViewModel(new RecordingStore());
        viewModel.SettingsService.ModelProviderProfiles = [Profile("vendor/model-c")];

        viewModel.OnRuntimeSettingsApplied(["AIService:Chat:ModelId"]);

        viewModel.AgentChatModelOptions[0].Label.Should().Be("vendor/model-c");
        viewModel.SelectedAgentChatModel!.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task CopyCommands_WriteMessageAndConversation()
    {
        var copied = new List<string>();
        var viewModel = CreateViewModel(new RecordingStore());
        viewModel.ClipboardWriter = text =>
        {
            copied.Add(text);
            return Task.CompletedTask;
        };
        var session = viewModel.SelectedAgentChatSession!;
        var reply = AgentChatMessageViewModel.Agent("Here is **the plan**.");
        session.AddMessage(new AgentChatMessageViewModel("You", "plan it", "10:00"));
        session.AddMessage(reply);

        viewModel.CopyAgentMessageCommand.Execute(reply);
        await WaitForAsync(() => reply.IsCopied ? reply : null);
        viewModel.CopyAgentChatCommand.Execute(null);
        await WaitForAsync(() => copied.Count > 1 ? copied : null);

        copied.Should().Equal("Here is **the plan**.", MainWindowViewModel.FormatConversation(session));
    }

    [Fact]
    public void FormatConversation_WritesMarkdownWithToolCallsOnOneLine()
    {
        var session = AgentChatSessionViewModel.Create("Build fix", "summary", "now");
        session.AddMessage(new AgentChatMessageViewModel("You", "fix the build", "10:00"));
        var step = AgentChatMessageViewModel.ToolStep("c1", "shell_run", "Run Shell Command", "{}", ToolRisk.Execute);
        step.CompleteTool(true, "ok");
        session.AddMessage(step);
        session.AddMessage(AgentChatMessageViewModel.Agent("Fixed.\n"));

        var text = MainWindowViewModel.FormatConversation(session);

        text.Should().Be(string.Join(Environment.NewLine,
            "# Build fix",
            "",
            "**You:**",
            "fix the build",
            "",
            "> `shell_run` · Done",
            "",
            "**Kam:**",
            "Fixed.") + Environment.NewLine);
    }

    [Fact]
    public void DescribeSettingsChange_NamesWhatChangedAndWhatNeedsARestart()
    {
        MainWindowViewModel.DescribeSettingsChange(
            ["AIService:ModelId", "AIService:Chat:ModelId", "WebResearch:SearchApiKey", "McpOptions:TodoistApiKey", "Email:Host"])
            .Should().Be("models, web search, Todoist, email (after restart)");
    }

    [Fact]
    public async Task ApprovalRequest_NotifiesAndAnsweringDismisses()
    {
        var runtime = new ApprovalRuntime();
        var viewModel = CreateViewModel(new RecordingStore(), runtime);
        var notifier = new RecordingNotifier();
        viewModel.SetApprovalNotifier(notifier);
        var session = viewModel.SelectedAgentChatSession!;
        viewModel.CommandInputText = "run the build";

        var turn = viewModel.SubmitCommandInputAsync();
        var step = await WaitForAsync(() => session.Messages.FirstOrDefault(m => m.IsAwaitingApproval));

        var notice = notifier.Shown.Should().ContainSingle().Subject;
        notice.RequestId.Should().Be("req-1");
        notice.ToolName.Should().Be("Run Shell Command");
        notice.ThreadTitle.Should().Be(session.Title);

        viewModel.ApproveToolCallCommand.Execute(step);
        await turn;

        notifier.Dismissed.Should().Contain("req-1");
    }

    private MainWindowViewModel CreateViewModel(RecordingStore store, IAgentRuntime? runtime = null)
    {
        var settings = new JsonSettingsService(Path.Combine(_settingsDirectory, Guid.NewGuid().ToString("N")));
        _settings.Add(settings);
        settings.ModelProviderProfiles = [Profile("vendor/model-a")];
        settings.ActiveChatProfileId = "primary";
        settings.ActivePlannerProfileId = "primary";

        var viewModel = new MainWindowViewModel(settings);
        viewModel.SetAgentRuntime(runtime ?? new TextRuntime("ok"), new AskPermissions(), store);
        return viewModel;
    }

    private static ModelProviderProfile Profile(string modelId) => new()
    {
        Id = "primary",
        Provider = ModelProviderType.OpenRouter,
        DisplayName = "OpenRouter",
        Endpoint = "https://openrouter.ai/api/v1",
        ApiKey = "sk-test",
        ModelId = modelId,
        Roles = [ModelProviderRole.Planner, ModelProviderRole.Chat],
        Enabled = true
    };

    private static List<string> VisibleTitles(MainWindowViewModel viewModel) =>
        viewModel.AgentChatSessions.Where(session => session.IsVisibleInList).Select(session => session.Title).ToList();

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

    private sealed class RecordingStore : IAgentSessionStore
    {
        public RecordingStore(params string[] savedTitles)
        {
            for (var index = 0; index < savedTitles.Length; index++)
            {
                Saved.Add(new AgentSessionSummary(
                    $"s{index + 1}",
                    savedTitles[index],
                    DateTimeOffset.Now.AddHours(-(index + 1)),
                    4,
                    null));
            }
        }

        public List<AgentSessionSummary> Saved { get; } = [];

        public List<(string Id, string? Title)> Renames { get; } = [];

        public List<(string Id, string? Model)> Models { get; } = [];

        public List<string> Deletes { get; } = [];

        public Task<IReadOnlyList<ChatMessage>> LoadAsync(string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>([]);

        public Task SaveAsync(string sessionId, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<AgentSessionSummary>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentSessionSummary>>(Saved.ToList());

        public Task<AgentSessionSummary?> GetSummaryAsync(string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved.FirstOrDefault(summary => summary.Id == sessionId));

        public Task RenameAsync(string sessionId, string? title, CancellationToken cancellationToken = default)
        {
            Renames.Add((sessionId, title));
            return Task.CompletedTask;
        }

        public Task SetModelAsync(string sessionId, string? modelId, CancellationToken cancellationToken = default)
        {
            Models.Add((sessionId, modelId));
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            Deletes.Add(sessionId);
            return Task.CompletedTask;
        }
    }

    private sealed class TextRuntime(string reply) : IAgentRuntime
    {
        public async IAsyncEnumerable<AgentEvent> RunTurnAsync(
            string sessionId,
            string userMessage,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new AgentTextDelta(sessionId, reply);
            yield return new AgentTurnCompleted(sessionId, reply, 0, 0, 0);
        }

        public bool ResolveApproval(string requestId, bool approved, bool alwaysAllow = false) => false;
    }

    private sealed class ApprovalRuntime : IAgentRuntime
    {
        private readonly TaskCompletionSource<bool> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<AgentEvent> RunTurnAsync(
            string sessionId,
            string userMessage,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new AgentToolCallStarted(sessionId, "c1", "shell_run", "Run Shell Command", "{}", ToolRisk.Execute);
            yield return new AgentApprovalRequested(sessionId, "req-1", "c1", "shell_run", "Run Shell Command", "{}", ToolRisk.Execute);
            var approved = await _answer.Task.WaitAsync(cancellationToken);
            yield return new AgentToolCallCompleted(sessionId, "c1", "shell_run", approved, "ok");
            yield return new AgentTurnCompleted(sessionId, "done", 1, 0, 0);
        }

        public bool ResolveApproval(string requestId, bool approved, bool alwaysAllow = false) => _answer.TrySetResult(approved);
    }

    private sealed class RecordingNotifier : IApprovalNotifier
    {
        public List<ApprovalNotice> Shown { get; } = [];

        public List<string> Dismissed { get; } = [];

        public void Show(ApprovalNotice notice) => Shown.Add(notice);

        public void Dismiss(string requestId) => Dismissed.Add(requestId);
    }

    private sealed class AskPermissions : IToolPermissionService
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
}
