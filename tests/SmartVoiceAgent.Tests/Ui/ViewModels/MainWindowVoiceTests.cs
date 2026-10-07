using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.AI;
using SmartVoiceAgent.Tests.Ui.Services;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class MainWindowVoiceTests : IDisposable
{
    private readonly string _settingsDirectory = Path.Combine(
        Path.GetTempPath(),
        "kam-main-window-voice-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SetVoiceAssistant_LeavesTheWakePhraseOffUntilTurnedOn()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        var fakes = new VoiceFakes();
        var viewModel = new MainWindowViewModel(settings);

        viewModel.SetVoiceAssistant(fakes.CreateAssistant());

        viewModel.IsVoiceAvailable.Should().BeTrue();
        viewModel.IsWakeWordEnabled.Should().BeFalse();
        viewModel.IsVoiceStatusVisible.Should().BeFalse("the composer stays quiet while voice waits for the talk button");
        fakes.WakeWord.StartCount.Should().Be(0);
    }

    [Fact]
    public async Task SetVoiceAssistant_WithTheWakePhraseSaved_StartsListening()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        settings.WakeWordEnabled = true;
        var fakes = new VoiceFakes();
        var viewModel = new MainWindowViewModel(settings);

        viewModel.SetVoiceAssistant(fakes.CreateAssistant());

        await VoiceFakes.WaitUntilAsync(() => fakes.WakeWord.IsListening, "the wake phrase listener starts");
    }

    [Fact]
    public async Task ToggleWakeWordCommand_SavesTheChoiceAndTurnsTheListenerOnAndOff()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        var fakes = new VoiceFakes();
        var viewModel = new MainWindowViewModel(settings);
        viewModel.SetVoiceAssistant(fakes.CreateAssistant());

        viewModel.ToggleWakeWordCommand.Execute(null);

        settings.WakeWordEnabled.Should().BeTrue();
        await VoiceFakes.WaitUntilAsync(() => fakes.WakeWord.IsListening, "the listener starts");
        fakes.WakeWord.StartCount.Should().Be(1, "saving the choice must not start the listener twice");

        viewModel.ToggleWakeWordCommand.Execute(null);

        settings.WakeWordEnabled.Should().BeFalse();
        await VoiceFakes.WaitUntilAsync(() => !fakes.WakeWord.IsListening, "the listener stops");
    }

    [Fact]
    public async Task WakeWordEnabledSetting_ChangedInSettings_TurnsTheListenerOn()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        var fakes = new VoiceFakes();
        var viewModel = new MainWindowViewModel(settings);
        viewModel.SetVoiceAssistant(fakes.CreateAssistant());

        settings.WakeWordEnabled = true;

        await VoiceFakes.WaitUntilAsync(() => fakes.WakeWord.IsListening, "the listener starts");
    }

    [Fact]
    public void ToggleSpokenRepliesCommand_TurnsReadingOffAndRestoresTheLastMode()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        settings.SpokenReplies = MainWindowViewModel.SpokenRepliesAll;
        var viewModel = new MainWindowViewModel(settings);

        viewModel.IsSpokenRepliesOn.Should().BeTrue();
        viewModel.SpokenRepliesToolTip.Should().Be("Turn off reading replies aloud");

        viewModel.ToggleSpokenRepliesCommand.Execute(null);

        settings.SpokenReplies.Should().Be(MainWindowViewModel.SpokenRepliesOff);
        viewModel.IsSpokenRepliesOn.Should().BeFalse();
        viewModel.SpokenRepliesToolTip.Should().Be("Read replies aloud");

        viewModel.ToggleSpokenRepliesCommand.Execute(null);

        settings.SpokenReplies.Should().Be(MainWindowViewModel.SpokenRepliesAll);
        viewModel.IsSpokenRepliesOn.Should().BeTrue();
    }

    [Fact]
    public void ToggleSpokenRepliesCommand_WhenOffAtStart_TurnsOnRepliesToVoiceCommands()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        settings.SpokenReplies = MainWindowViewModel.SpokenRepliesOff;
        var viewModel = new MainWindowViewModel(settings);

        viewModel.ToggleSpokenRepliesCommand.Execute(null);

        settings.SpokenReplies.Should().Be(MainWindowViewModel.SpokenRepliesVoice);
    }

    [Fact]
    public void ToggleSpokenRepliesCommand_WhileReading_StopsReading()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        var fakes = new VoiceFakes();
        var viewModel = new MainWindowViewModel(settings);
        var voice = fakes.CreateAssistant();
        viewModel.SetVoiceAssistant(voice);
        _ = voice.SpeakAsync("Opened Spotify.");

        viewModel.ToggleSpokenRepliesCommand.Execute(null);

        fakes.Speech.StopCount.Should().Be(1);
    }

    [Fact]
    public void SpokenRepliesSetting_ChangedInSettings_UpdatesTheToggle()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        var viewModel = new MainWindowViewModel(settings);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        settings.SpokenReplies = MainWindowViewModel.SpokenRepliesOff;

        viewModel.IsSpokenRepliesOn.Should().BeFalse();
        changed.Should().Contain(nameof(MainWindowViewModel.IsSpokenRepliesOn));
    }

    [Fact]
    public void TalkToolTip_NamesTheShortcutFromSettings()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        var viewModel = new MainWindowViewModel(settings);

        viewModel.TalkToolTip.Should().Be("Voice isn't available on this computer");

        viewModel.SetVoiceAssistant(new VoiceFakes().CreateAssistant());
        viewModel.TalkToolTip.Should().Be("Talk (Ctrl+Alt+Space)");

        var changed = 0;
        viewModel.TalkShortcutChanged += (_, _) => changed++;
        settings.TalkShortcut = string.Empty;

        viewModel.TalkShortcut.Should().BeEmpty();
        viewModel.TalkToolTip.Should().Be("Talk");
        changed.Should().Be(1, "the window registers the new shortcut");
    }

    [Fact]
    public void ToggleTalk_WithoutVoice_OnlyLogs()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        var viewModel = new MainWindowViewModel(settings);

        viewModel.ToggleTalk();

        viewModel.IsVoiceAvailable.Should().BeFalse();
        viewModel.CancelVoice().Should().BeFalse();
    }

    [Fact]
    public void ReportTalkShortcutUnavailable_ShowsWhyInTheComposer()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        var viewModel = new MainWindowViewModel(settings);
        viewModel.SetVoiceAssistant(new VoiceFakes().CreateAssistant());

        viewModel.ReportTalkShortcutUnavailable("Ctrl+Alt+Space");

        viewModel.IsVoiceStatusVisible.Should().BeTrue();
        viewModel.VoiceStatusText.Should().Be("Ctrl+Alt+Space is taken by another app; pick another talk shortcut in Settings");
    }

    [Theory]
    [InlineData("Off", true, false)]
    [InlineData("Off", false, false)]
    [InlineData("Voice", true, true)]
    [InlineData("Voice", false, false)]
    [InlineData("All", false, true)]
    [InlineData("", true, true)]
    [InlineData(null, false, false)]
    public void ShouldReadReplyAloud_FollowsTheSpokenRepliesSetting(string? mode, bool fromVoice, bool expected)
    {
        MainWindowViewModel.ShouldReadReplyAloud(mode, fromVoice).Should().Be(expected);
    }

    [Fact]
    public void FormatVoiceState_DescribesEveryState()
    {
        MainWindowViewModel.FormatVoiceState(VoiceState.WakeListening, 0, "Hey Kam").Should().Be("Say “Hey Kam”");
        MainWindowViewModel.FormatVoiceState(VoiceState.Listening, 0, "Hey Kam").Should().Be("Listening…");
        MainWindowViewModel.FormatVoiceState(VoiceState.Preparing, 0.42, "Hey Kam").Should().StartWith("Downloading speech model 42");

        foreach (var state in Enum.GetValues<VoiceState>())
        {
            MainWindowViewModel.FormatVoiceState(state, 0, "Hey Kam").Should().NotStartWith("Voice.", "{0} needs text", state);
        }
    }

    [Fact]
    public void FormatVoiceProblem_HasTextForEveryProblem()
    {
        MainWindowViewModel.FormatVoiceProblem(VoiceProblem.None).Should().BeNull();
        foreach (var problem in Enum.GetValues<VoiceProblem>().Where(problem => problem != VoiceProblem.None))
        {
            MainWindowViewModel.FormatVoiceProblem(problem).Should().NotBeNullOrWhiteSpace()
                .And.NotStartWith("Voice.", "{0} needs text", problem);
        }
    }

    [Fact]
    public void HeaderStatus_WithoutTheAgent_PausesAndResumesTheCommandLoop()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        var viewModel = new MainWindowViewModel(settings);
        var host = new RecordingHostControl(isRunning: true);

        viewModel.SetVoiceAgentHostControl(host);

        viewModel.CurrentHeaderStatus.Should().Be(MainWindowViewModel.HeaderStatus.CommandModeOn);
        viewModel.StatusText.Should().Be("Command mode on");
        viewModel.StatusToolTip.Should().Contain("Click to pause");

        viewModel.HeaderStatusCommand.Execute(null);

        host.StopCount.Should().Be(1);
    }

    [Fact]
    public void HeaderStatus_WithAPausedCommandLoop_SaysSo()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        var viewModel = new MainWindowViewModel(settings);
        var host = new RecordingHostControl(isRunning: false);

        viewModel.SetVoiceAgentHostControl(host);
        viewModel.HeaderStatusCommand.Execute(null);

        viewModel.CurrentHeaderStatus.Should().Be(MainWindowViewModel.HeaderStatus.CommandModePaused);
        viewModel.StatusText.Should().Be("Command mode paused");
        host.StartCount.Should().Be(1);
    }

    [Fact]
    public void HeaderStatus_WithTheAgentAndNoModel_AsksForAModel()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        settings.ModelProviderProfiles = [];
        var viewModel = new MainWindowViewModel(settings);
        viewModel.SetVoiceAgentHostControl(new RecordingHostControl(isRunning: true));

        viewModel.SetAgentRuntime(new IdleRuntime(), new AllowAllPermissions(), new NoSessions());

        viewModel.CurrentHeaderStatus.Should().Be(MainWindowViewModel.HeaderStatus.NeedsModel);
        viewModel.StatusText.Should().Be("Set up a model");
        viewModel.StatusToolTip.Should().Contain("Settings");
    }

    [Fact]
    public void HeaderStatus_WithTheAgentAndAModel_IsReadyAndNamesTheModel()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        settings.ModelProviderProfiles =
        [
            new ModelProviderProfile
            {
                Id = "chat",
                Provider = ModelProviderType.OpenAI,
                DisplayName = "OpenAI",
                Endpoint = "https://api.openai.com/v1",
                ApiKey = "sk-test",
                ModelId = "gpt-5.2",
                Roles = [ModelProviderRole.Planner, ModelProviderRole.Chat],
                Enabled = true
            }
        ];
        var viewModel = new MainWindowViewModel(settings);

        viewModel.SetAgentRuntime(new IdleRuntime(), new AllowAllPermissions(), new NoSessions());

        viewModel.CurrentHeaderStatus.Should().Be(MainWindowViewModel.HeaderStatus.AgentReady);
        viewModel.StatusText.Should().Be("Ready");
        viewModel.StatusToolTip.Should().Contain("gpt-5.2");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_settingsDirectory))
            {
                Directory.Delete(_settingsDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed class RecordingHostControl(bool isRunning) : IVoiceAgentHostControl
    {
        public bool IsRunning { get; private set; } = isRunning;

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public event EventHandler<bool>? StateChanged;

        public Task StartAsync()
        {
            StartCount++;
            IsRunning = true;
            StateChanged?.Invoke(this, true);
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCount++;
            IsRunning = false;
            StateChanged?.Invoke(this, false);
            return Task.CompletedTask;
        }
    }

    private sealed class IdleRuntime : IAgentRuntime
    {
        public async IAsyncEnumerable<AgentEvent> RunTurnAsync(
            string sessionId,
            string userMessage,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public bool ResolveApproval(string requestId, bool approved, bool alwaysAllow = false) => false;
    }

    private sealed class AllowAllPermissions : IToolPermissionService
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

    private sealed class NoSessions : IAgentSessionStore
    {
        public Task<IReadOnlyList<ChatMessage>> LoadAsync(string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>([]);

        public Task SaveAsync(string sessionId, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<AgentSessionSummary>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentSessionSummary>>([]);

        public Task<AgentSessionSummary?> GetSummaryAsync(string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AgentSessionSummary?>(null);

        public Task RenameAsync(string sessionId, string? title, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetModelAsync(string sessionId, string? modelId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
