using FluentAssertions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.SlashCommands;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class MainWindowSlashCommandTests
{
    [Fact]
    public void CommandInputText_WhenSlashPrefix_ShowsSlashCommandSuggestions()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.SetSlashCommandService(new FakeSlashCommandService());

        viewModel.CommandInputText = "/";

        viewModel.IsSlashCommandPaletteVisible.Should().BeTrue();
        viewModel.SelectedSlashCommandIndex.Should().Be(0);
        viewModel.SlashCommandSuggestions[0].IsSelected.Should().BeTrue();
        viewModel.SlashCommandSuggestions.Select(command => command.Name)
            .Should()
            .Contain(["/help", "/plugins", "/status"]);
    }

    [Fact]
    public void CommandInputText_WhenSlashFilterIsTyped_FiltersSuggestions()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.SetSlashCommandService(new FakeSlashCommandService());

        viewModel.CommandInputText = "/pl";

        viewModel.IsSlashCommandPaletteVisible.Should().BeTrue();
        viewModel.SlashCommandSuggestions.Select(command => command.Name)
            .Should()
            .Equal("/plugins");
    }

    [Fact]
    public void CommandInputText_WhenUpdateFilterIsTyped_ShowsUpdateSuggestion()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.SetSlashCommandService(new FakeSlashCommandService());

        viewModel.CommandInputText = "/up";

        viewModel.IsSlashCommandPaletteVisible.Should().BeTrue();
        viewModel.SlashCommandSuggestions.Select(command => command.Name)
            .Should()
            .Equal("/update");
    }

    [Fact]
    public void CommandInputText_WhenPlainTextIsTyped_HidesSlashCommandSuggestions()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.SetSlashCommandService(new FakeSlashCommandService());
        viewModel.CommandInputText = "/";

        viewModel.CommandInputText = "open spotify";

        viewModel.IsSlashCommandPaletteVisible.Should().BeFalse();
        viewModel.SlashCommandSuggestions.Should().BeEmpty();
        viewModel.SelectedSlashCommandIndex.Should().Be(-1);
    }

    [Fact]
    public void CommandInputText_WhenServiceReturnsManySuggestions_CapsVisibleSuggestions()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.SetSlashCommandService(new ManySlashCommandService());

        viewModel.CommandInputText = "/";

        viewModel.SlashCommandSuggestions.Should().HaveCount(8);
    }

    [Fact]
    public void SetSlashCommandService_WhenInputAlreadyHasSlashPrefix_RefreshesSuggestions()
    {
        var viewModel = new MainWindowViewModel
        {
            CommandInputText = "/pl"
        };

        viewModel.SetSlashCommandService(new FakeSlashCommandService());

        viewModel.IsSlashCommandPaletteVisible.Should().BeTrue();
        viewModel.SlashCommandSuggestions.Select(command => command.Name)
            .Should()
            .Equal("/plugins");
    }

    [Fact]
    public void AcceptFirstSlashCommandSuggestion_FillsCommandInput()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.SetSlashCommandService(new FakeSlashCommandService());
        viewModel.CommandInputText = "/pl";

        var accepted = viewModel.AcceptFirstSlashCommandSuggestion();

        accepted.Should().BeTrue();
        viewModel.CommandInputText.Should().Be("/plugins ");
        viewModel.IsSlashCommandPaletteVisible.Should().BeFalse();
    }

    [Fact]
    public void MoveSlashCommandSelection_WhenPaletteIsVisible_WrapsThroughSuggestions()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.SetSlashCommandService(new FakeSlashCommandService());
        viewModel.CommandInputText = "/";

        viewModel.MoveSlashCommandSelection(-1).Should().BeTrue();

        viewModel.SelectedSlashCommandIndex.Should().Be(3);
        viewModel.SlashCommandSuggestions[3].Name.Should().Be("/update");
        viewModel.SlashCommandSuggestions[3].IsSelected.Should().BeTrue();

        viewModel.MoveSlashCommandSelection(1).Should().BeTrue();

        viewModel.SelectedSlashCommandIndex.Should().Be(0);
        viewModel.SlashCommandSuggestions[0].Name.Should().Be("/help");
        viewModel.SlashCommandSuggestions[0].IsSelected.Should().BeTrue();
    }

    [Fact]
    public void AcceptSelectedSlashCommandSuggestion_UsesCurrentSelection()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.SetSlashCommandService(new FakeSlashCommandService());
        viewModel.CommandInputText = "/";

        viewModel.MoveSlashCommandSelection(2);
        var accepted = viewModel.AcceptSelectedSlashCommandSuggestion();

        accepted.Should().BeTrue();
        viewModel.CommandInputText.Should().Be("/status ");
        viewModel.IsSlashCommandPaletteVisible.Should().BeFalse();
        viewModel.SelectedSlashCommandIndex.Should().Be(-1);
    }

    [Fact]
    public void SelectSlashCommandCommand_WhenParameterIsNull_DoesNotChangeInput()
    {
        var viewModel = new MainWindowViewModel
        {
            CommandInputText = "/pl"
        };
        viewModel.SetSlashCommandService(new FakeSlashCommandService());

        viewModel.SelectSlashCommandCommand.Execute(null);

        viewModel.CommandInputText.Should().Be("/pl");
        viewModel.IsSlashCommandPaletteVisible.Should().BeTrue();
    }

    [Fact]
    public async Task SubmitCommandInputAsync_WithSlashCommand_ExecutesSlashCommandWithoutSubmittingAgentCommand()
    {
        var slashService = new FakeSlashCommandService();
        var commandInput = new RecordingCommandInputService();
        var viewModel = new MainWindowViewModel();
        viewModel.SetCommandInputService(commandInput);
        viewModel.SetSlashCommandService(slashService);
        viewModel.CommandInputText = "/status";

        await viewModel.SubmitCommandInputAsync();

        slashService.ExecutedInput.Should().Be("/status");
        commandInput.SubmittedCommands.Should().BeEmpty();
        viewModel.CommandInputText.Should().BeEmpty();
        viewModel.IsSlashCommandPaletteVisible.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitCommandInputAsync_WithPluginsSlashCommand_UsesSlashServiceHealthCommand()
    {
        var slashService = new FakeSlashCommandService();
        var commandInput = new RecordingCommandInputService();
        var viewModel = new MainWindowViewModel();
        viewModel.SetCommandInputService(commandInput);
        viewModel.SetSlashCommandService(slashService);
        viewModel.CommandInputText = "/plugins";

        await viewModel.SubmitCommandInputAsync();

        slashService.ExecutedInput.Should().Be("/plugins");
        commandInput.SubmittedCommands.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitCommandInputAsync_WithPlainText_SubmitsAgentCommand()
    {
        var commandInput = new RecordingCommandInputService();
        var viewModel = new MainWindowViewModel();
        viewModel.SetCommandInputService(commandInput);
        viewModel.SetSlashCommandService(new FakeSlashCommandService());
        viewModel.CommandInputText = "open spotify";

        await viewModel.SubmitCommandInputAsync();

        commandInput.SubmittedCommands.Should().Equal("open spotify");
        viewModel.CommandInputText.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitCommandInputAsync_WithPlainTextAndNoCommandInput_LeavesInputVisible()
    {
        var viewModel = new MainWindowViewModel
        {
            CommandInputText = "open spotify"
        };
        viewModel.SetSlashCommandService(new FakeSlashCommandService());

        await viewModel.SubmitCommandInputAsync();

        viewModel.CommandInputText.Should().Be("open spotify");
    }

    [Fact]
    public void ActivityPanelMode_DefaultsToAgentRunsAndCanSwitchTabs()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.SelectedActivityPanelMode.Should().Be(ActivityPanelMode.Runs);
        viewModel.IsActivityRunsPanelVisible.Should().BeTrue();
        viewModel.IsActivityContextPanelVisible.Should().BeFalse();
        viewModel.IsActivityEventsPanelVisible.Should().BeFalse();

        viewModel.ShowContextCommand.Execute(null);

        viewModel.SelectedActivityPanelMode.Should().Be(ActivityPanelMode.Context);
        viewModel.IsActivityContextPanelVisible.Should().BeTrue();

        viewModel.ShowEventsCommand.Execute(null);

        viewModel.SelectedActivityPanelMode.Should().Be(ActivityPanelMode.Events);
        viewModel.IsActivityEventsPanelVisible.Should().BeTrue();

        viewModel.ShowRunsCommand.Execute(null);

        viewModel.SelectedActivityPanelMode.Should().Be(ActivityPanelMode.Runs);
        viewModel.IsActivityRunsPanelVisible.Should().BeTrue();
    }

    [Fact]
    public void AgentChatSessions_DefaultToOneActiveWorkspaceThread()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.AgentChatSessions.Should().ContainSingle();
        viewModel.SelectedAgentChatSession.Should().Be(viewModel.AgentChatSessions[0]);
        viewModel.SelectedAgentChatSession!.IsSelected.Should().BeTrue();
        viewModel.SelectedAgentChatSession.Title.Should().Be("Workspace chat");
        viewModel.HasAgentChatMessages.Should().BeFalse();
        viewModel.AgentChatSessionCountText.Should().Be("1 thread");
        viewModel.SelectedAgentChatMessageCountText.Should().Be("No messages");
        viewModel.IsChatWorkbenchVisible.Should().BeTrue();
        viewModel.IsPageHostVisible.Should().BeFalse();
    }

    [Fact]
    public void NewAgentChatCommand_CreatesAndSelectsFreshThread()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.NewAgentChatCommand.Execute(null);

        viewModel.AgentChatSessions.Should().HaveCount(2);
        viewModel.AgentChatSessionCountText.Should().Be("2 threads");
        viewModel.SelectedAgentChatSession.Should().Be(viewModel.AgentChatSessions[0]);
        viewModel.AgentChatSessions[0].Title.Should().Be("New chat");
        viewModel.AgentChatSessions[0].Messages.Should().BeEmpty();
        viewModel.AgentChatSessions[0].IsSelected.Should().BeTrue();
        viewModel.AgentChatSessions[1].IsSelected.Should().BeFalse();
    }

    [Fact]
    public void SelectAgentChatCommand_SwitchesActiveThread()
    {
        var viewModel = new MainWindowViewModel();
        var original = viewModel.SelectedAgentChatSession!;
        viewModel.NewAgentChatCommand.Execute(null);

        viewModel.SelectAgentChatCommand.Execute(original);

        viewModel.SelectedAgentChatSession.Should().Be(original);
        original.IsSelected.Should().BeTrue();
        viewModel.AgentChatSessions[0].IsSelected.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitCommandInputAsync_WithPlainText_AddsMessageToActiveThread()
    {
        var commandInput = new RecordingCommandInputService();
        var viewModel = new MainWindowViewModel();
        viewModel.SetCommandInputService(commandInput);
        viewModel.SetSlashCommandService(new FakeSlashCommandService());
        viewModel.CommandInputText = "review the UI";

        await viewModel.SubmitCommandInputAsync();

        viewModel.SelectedAgentChatSession!.Title.Should().Be("review the UI");
        viewModel.SelectedAgentChatSession.Messages.Last().Role.Should().Be("You");
        viewModel.SelectedAgentChatSession.Messages.Last().Content.Should().Be("review the UI");
        viewModel.SelectedAgentChatSession.MessageCountText.Should().Be("1 message");
        viewModel.SelectedAgentChatMessageCountText.Should().Be("1 message");
    }

    [Fact]
    public void AddComposerAttachmentPaths_AddsFileChipsAndAllowsRemoval()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.AddComposerAttachmentPaths([
            @"D:\Workstation\Kam\Kam\src\Program.cs",
            @"D:\Workstation\Kam\Kam\README.md"
        ]);

        viewModel.HasComposerAttachments.Should().BeTrue();
        viewModel.ActiveComposerContextText.Should().Be("2 files");
        viewModel.ComposerAttachments.Select(file => file.FileName)
            .Should()
            .Equal("Program.cs", "README.md");
        viewModel.ComposerAttachments[0].DisplayPath.Should().Be(@"...\src\Program.cs");

        viewModel.RemoveComposerAttachmentCommand.Execute(viewModel.ComposerAttachments[0]);

        viewModel.ComposerAttachments.Select(file => file.FileName).Should().Equal("README.md");
        viewModel.ActiveComposerContextText.Should().Be("1 file");

        viewModel.ClearComposerAttachmentsCommand.Execute(null);

        viewModel.HasComposerAttachments.Should().BeFalse();
        viewModel.ComposerAttachments.Should().BeEmpty();
        viewModel.ActiveComposerContextText.Should().Be("No files");
    }

    [Fact]
    public async Task SubmitCommandInputAsync_WithPlainTextAndAttachments_AppendsContextFilesAndClearsComposer()
    {
        var commandInput = new RecordingCommandInputService();
        var viewModel = new MainWindowViewModel();
        viewModel.SetCommandInputService(commandInput);
        viewModel.SetSlashCommandService(new FakeSlashCommandService());
        viewModel.CommandInputText = "review this area";
        viewModel.AddComposerAttachmentPaths([
            @"D:\Workstation\Kam\Kam\src\Program.cs",
            @"D:\Workstation\Kam\Kam\README.md"
        ]);

        await viewModel.SubmitCommandInputAsync();

        commandInput.SubmittedCommands.Should().ContainSingle().Which.Should().Be(
            "review this area"
            + Environment.NewLine
            + Environment.NewLine
            + "Context files:"
            + Environment.NewLine
            + "- D:\\Workstation\\Kam\\Kam\\src\\Program.cs"
            + Environment.NewLine
            + "- D:\\Workstation\\Kam\\Kam\\README.md");
        viewModel.CommandInputText.Should().BeEmpty();
        viewModel.ComposerAttachments.Should().BeEmpty();
        viewModel.HasComposerAttachments.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitCommandInputAsync_WithSlashCommand_DoesNotAppendAttachmentContext()
    {
        var slashService = new FakeSlashCommandService();
        var commandInput = new RecordingCommandInputService();
        var viewModel = new MainWindowViewModel();
        viewModel.SetCommandInputService(commandInput);
        viewModel.SetSlashCommandService(slashService);
        viewModel.CommandInputText = "/status";
        viewModel.AddComposerAttachmentPath(@"D:\Workstation\Kam\Kam\src\Program.cs");

        await viewModel.SubmitCommandInputAsync();

        slashService.ExecutedInput.Should().Be("/status");
        commandInput.SubmittedCommands.Should().BeEmpty();
        viewModel.ComposerAttachments.Should().ContainSingle();
    }

    private class FakeSlashCommandService : ISlashCommandService
    {
        private static readonly SlashCommandDefinition[] Commands =
        [
            new("/help", "Show commands.", "/help", "General"),
            new("/plugins", "Show plugin health.", "/plugins", "Skills"),
            new("/status", "Show runtime status.", "/status", "Runtime"),
            new("/update", "Check for updates.", "/update", "Updates")
        ];

        public string? ExecutedInput { get; private set; }

        public virtual IReadOnlyList<SlashCommandDefinition> GetCommands()
        {
            return Commands;
        }

        public virtual IReadOnlyList<SlashCommandDefinition> GetSuggestions(string input)
        {
            var filter = input.TrimStart('/').Trim();
            return Commands
                .Where(command => string.IsNullOrWhiteSpace(filter)
                    || command.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public bool IsSlashCommand(string input)
        {
            return !string.IsNullOrWhiteSpace(input)
                && input.TrimStart().StartsWith("/", StringComparison.Ordinal);
        }

        public Task<SlashCommandResult> ExecuteAsync(
            string input,
            CancellationToken cancellationToken = default)
        {
            ExecutedInput = input;
            return Task.FromResult(SlashCommandResult.Succeeded(input, "slash ok"));
        }
    }

    private sealed class ManySlashCommandService : FakeSlashCommandService
    {
        private static readonly SlashCommandDefinition[] ManyCommands = Enumerable
            .Range(1, 12)
            .Select(index => new SlashCommandDefinition(
                $"/command{index}",
                $"Command {index}",
                $"/command{index}",
                "General"))
            .ToArray();

        public override IReadOnlyList<SlashCommandDefinition> GetSuggestions(string input)
        {
            return ManyCommands;
        }
    }

    private sealed class RecordingCommandInputService : ICommandInputService
    {
        public event EventHandler<CommandResultEventArgs>? OnResult;

        public List<string> SubmittedCommands { get; } = [];

        public void SubmitCommand(string command)
        {
            SubmittedCommands.Add(command);
        }

        public Task<string> ReadCommandAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void PublishResult(string command, string result, bool success = true)
        {
            OnResult?.Invoke(this, new CommandResultEventArgs
            {
                Command = command,
                Result = result,
                Success = success
            });
        }
    }
}
