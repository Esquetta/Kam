using FluentAssertions;
using Moq;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents.Extensions;
using SmartVoiceAgent.Ui.ViewModels.PageModels;
using System.Reactive.Linq;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class PromptsViewModelTests
{
    private const string Folder = "/virtual/commands";

    private readonly Mock<IAgentCommandCatalog> _catalog = new();
    private readonly List<AgentCommandInfo> _commands =
    [
        new("review", "Review a change", "[file]", "/virtual/commands/review.md", "user"),
        new("deploy", "Ship it", "", "/ws/.kam/commands/deploy.md", "workspace"),
        new("lint", "Lint the repo", "", "/plugins/qa/commands/lint.md", "plugin:qa")
    ];
    private readonly Dictionary<string, string> _disk = new()
    {
        ["/virtual/commands/review.md"] = "---\ndescription: Review a change\nargument-hint: [file]\n---\n\nReview $ARGUMENTS carefully.\n",
        ["/ws/.kam/commands/deploy.md"] = "Deploy now.",
        ["/plugins/qa/commands/lint.md"] = ""
    };
    private readonly List<string> _opened = [];
    private readonly List<string> _used = [];
    private readonly List<string> _createdDirectories = [];

    public PromptsViewModelTests()
    {
        _catalog.SetupGet(c => c.UserCommandsDirectory).Returns(Folder);
        _catalog.Setup(c => c.GetCommands()).Returns(() => _commands.ToList());
    }

    private PromptsFileHooks Hooks(Action<string>? open = null) => new(
        (path, max) => _disk.TryGetValue(path, out var text)
            ? text[..Math.Min(text.Length, max)]
            : throw new FileNotFoundException("missing", path),
        _disk.ContainsKey,
        (path, text) =>
        {
            _disk.Add(path, text);
            _commands.Add(new AgentCommandInfo(Path.GetFileNameWithoutExtension(path), "Describe what this prompt does", "", path, "user"));
        },
        _createdDirectories.Add,
        open ?? _opened.Add);

    private PromptsViewModel Create(IAgentCommandCatalog? catalog = null, Action<string>? open = null)
        => new(catalog ?? _catalog.Object, _used.Add, Hooks(open));

    [Fact]
    public void Groups_UserFirstThenWorkspaceThenPlugin_AndFirstPromptIsSelected()
    {
        using var vm = Create();

        vm.Groups.Select(g => g.Title).Should().Equal("Yours", "This workspace", "qa");
        vm.SelectedPrompt!.Info.Name.Should().Be("review");
        vm.SelectedPrompt.IsSelected.Should().BeTrue();
        vm.HasPrompts.Should().BeTrue();
        vm.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Preview_StripsFrontMatter()
    {
        using var vm = Create();

        vm.PreviewText.Should().Be("Review $ARGUMENTS carefully.");
    }

    [Fact]
    public void Preview_CapsLongBodies()
    {
        _disk["/virtual/commands/review.md"] = new string('x', 9000);
        using var vm = Create();

        vm.PreviewText.Length.Should().BeLessThanOrEqualTo(4002);
        vm.PreviewText.Should().EndWith("…");
    }

    [Fact]
    public void Preview_ReportsReadErrorsInsteadOfThrowing()
    {
        _disk.Remove("/virtual/commands/review.md");
        using var vm = Create();

        vm.PreviewText.Should().StartWith("The file could not be read");
    }

    [Fact]
    public void Preview_EmptyFile_ShowsEmptyNote()
    {
        using var vm = Create();
        vm.Groups[2].Items[0].SelectCommand.Execute().Subscribe();

        vm.PreviewText.Should().Be("This file has no prompt text yet.");
    }

    [Theory]
    [InlineData("---\na: b\n---\nBody", "Body")]
    [InlineData("---\r\na: b\r\n---\r\nBody", "Body")]
    [InlineData("No front matter", "No front matter")]
    [InlineData("---\nunterminated", "---\nunterminated")]
    public void StripFrontMatter_HandlesShapes(string text, string expected)
    {
        PromptsViewModel.StripFrontMatter(text).Trim().Should().Be(expected);
    }

    [Fact]
    public void Search_FiltersByNameAndDescription_AndReportsNoMatches()
    {
        using var vm = Create();

        vm.SearchText = "ship";
        vm.Groups.Should().ContainSingle().Which.Items.Single().Info.Name.Should().Be("deploy");

        vm.SearchText = "zzz";
        vm.Groups.Should().BeEmpty();
        vm.HasNoMatches.Should().BeTrue();
        vm.HasPrompts.Should().BeTrue();
    }

    [Fact]
    public void UseInNewTask_PassesSlashNameAndSpace()
    {
        using var vm = Create();

        vm.UseInNewTaskCommand.Execute().Subscribe();

        _used.Should().Equal("/review ");
    }

    [Fact]
    public void OpenFile_UsesShellHook_AndFailureBecomesMessage()
    {
        using var vm = Create();
        vm.OpenFileCommand.Execute().Subscribe();
        _opened.Should().Equal("/virtual/commands/review.md");

        using var failing = Create(open: _ => throw new InvalidOperationException("no handler"));
        failing.OpenFileCommand.Execute().Subscribe();
        failing.Message.Should().Contain("no handler");
        failing.HasMessage.Should().BeTrue();
    }

    [Fact]
    public void OpenFolder_CreatesDirectoryAndOpensIt()
    {
        using var vm = Create();

        vm.OpenFolderCommand.Execute().Subscribe();

        _createdDirectories.Should().Equal(Folder);
        _opened.Should().Equal(Folder);
    }

    [Fact]
    public void NewPrompt_CreatesTemplate_SelectsAndOpensIt()
    {
        using var vm = Create();

        vm.NewPromptCommand.Execute().Subscribe();

        var path = Path.Combine(Folder, "new-prompt.md");
        _disk[path].Should().StartWith("---\ndescription:").And.Contain("$ARGUMENTS");
        vm.SelectedPrompt!.FilePath.Should().Be(path);
        _opened.Should().Equal(path);
    }

    [Fact]
    public void NewPrompt_PicksNextFreeName()
    {
        _disk[Path.Combine(Folder, "new-prompt.md")] = "taken";
        using var vm = Create();

        vm.NewPromptCommand.Execute().Subscribe();

        _disk.Should().ContainKey(Path.Combine(Folder, "new-prompt-2.md"));
    }

    [Fact]
    public void EmptyCatalog_ShowsEmptyState()
    {
        _commands.Clear();
        using var vm = Create();

        vm.IsEmpty.Should().BeTrue();
        vm.HasPrompts.Should().BeFalse();
        vm.SelectedPrompt.Should().BeNull();
        vm.UserDirectory.Should().Be(Folder);
        _used.Should().BeEmpty();
    }

    [Fact]
    public void NullCatalog_IsUnavailable_AndActionsAreDisabled()
    {
        using var vm = new PromptsViewModel(null, _used.Add, Hooks());

        vm.IsUnavailable.Should().BeTrue();
        vm.IsEmpty.Should().BeFalse();
        vm.Groups.Should().BeEmpty();
        vm.NewPromptCommand.CanExecute.FirstAsync().Wait().Should().BeFalse();
        vm.OpenFolderCommand.CanExecute.FirstAsync().Wait().Should().BeFalse();
        vm.UseInNewTaskCommand.CanExecute.FirstAsync().Wait().Should().BeFalse();
    }

    [Fact]
    public void OnNavigatedTo_RefreshesAndKeepsSelection()
    {
        using var vm = Create();
        vm.Groups[1].Items[0].SelectCommand.Execute().Subscribe();
        _commands.Add(new AgentCommandInfo("zeta", "New one", "", "/virtual/commands/zeta.md", "user"));
        _disk["/virtual/commands/zeta.md"] = "z";

        vm.OnNavigatedTo();

        vm.Groups[0].Items.Select(i => i.Info.Name).Should().Contain("zeta");
        vm.SelectedPrompt!.Info.Name.Should().Be("deploy");
    }
}
