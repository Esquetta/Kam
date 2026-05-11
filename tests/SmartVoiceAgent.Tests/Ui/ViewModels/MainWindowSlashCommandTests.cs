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
