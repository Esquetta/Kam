using FluentAssertions;
using Moq;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents.Extensions;
using SmartVoiceAgent.Infrastructure.Agent.Extensions;
using SmartVoiceAgent.Infrastructure.Services;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Extensions;

public sealed class AgentCommandCatalogTests : IDisposable
{
    private readonly ExtensionTestFolder _folder = new();

    [Fact]
    public void GetCommands_UserWorkspaceAndPlugins_WithNamespacesForConflicts()
    {
        _folder.Write("user/review.md", "---\ndescription: Review the diff\nargument-hint: [file]\n---\nReview $ARGUMENTS");
        _folder.Write("user/git/pr.md", "# Open a pull request\n\nOpen a PR.");
        _folder.Write("work/.claude/commands/deploy.md", "Deploy to staging.");
        _folder.Write("plugin/commands/review.md", "Plugin review");
        _folder.Write("plugin/commands/lint.md", "Lint everything");
        var catalog = new AgentCommandCatalog(
            _folder.Path("user"),
            PluginsWith(_folder.Path("plugin", "commands", "review.md"), _folder.Path("plugin", "commands", "lint.md")),
            () => _folder.Path("work"));

        var commands = catalog.GetCommands();

        commands.Select(command => $"{command.Name}|{command.Source}|{command.Description}|{command.ArgumentHint}").Should().Equal(
            "git:pr|user|Open a pull request|",
            "review|user|Review the diff|[file]",
            "deploy|workspace|Deploy to staging.|",
            "tools:review|plugin:tools|Plugin review|",
            "lint|plugin:tools|Lint everything|");
    }

    [Fact]
    public void TryExpand_FillsArguments()
    {
        _folder.Write("user/review.md", "---\ndescription: d\n---\nReview $ARGUMENTS carefully.");
        var catalog = new AgentCommandCatalog(_folder.Path("user"));

        catalog.TryExpand("/review src/App.cs", out var prompt).Should().BeTrue();

        prompt.Should().Be("Review src/App.cs carefully.");
        catalog.TryExpand("/REVIEW", out var empty).Should().BeTrue();
        empty.Should().Be("Review  carefully.");
        catalog.TryExpand("/unknown x", out _).Should().BeFalse();
        catalog.TryExpand("review", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("Fix issue #$1 with priority $2.", "123 high", "Fix issue #123 with priority high.")]
    [InlineData("Rename $1 to $2.", "\"old name\" new", "Rename old name to new.")]
    [InlineData("Write tests.", "for the parser", "Write tests.\n\nARGUMENTS: for the parser")]
    [InlineData("Write tests.", "", "Write tests.")]
    [InlineData("Use $1 and $3.", "a", "Use a and .")]
    public void Expand_PositionalAndAppendedArguments(string body, string arguments, string expected)
    {
        AgentCommandCatalog.Expand(body, arguments).Should().Be(expected);
    }

    [Fact]
    public void SlashCommandService_SuggestsMarkdownCommands_ButKeepsBuiltIns()
    {
        _folder.Write("user/triage.md", "---\ndescription: Triage new issues\nargument-hint: [label]\n---\nTriage");
        _folder.Write("user/help.md", "Custom help");
        var service = new SlashCommandService(agentCommands: new AgentCommandCatalog(_folder.Path("user")));

        var suggestions = service.GetSuggestions("/tria");

        suggestions.Should().Contain(command => command.Name == "/triage" && command.Usage == "/triage [label]" && command.Category == "Custom");
        service.GetSuggestions("/help").Where(command => command.Name == "/help").Should().ContainSingle()
            .Which.Category.Should().Be("General", "a Markdown command cannot replace a built-in one");
        service.GetCommands().Should().NotContain(command => command.Name == "/triage");
    }

    [Fact]
    public void SlashCommandService_TypingInPalette_ReadsCommandFilesOnce()
    {
        var commands = new CountingCommands();
        var service = new SlashCommandService(agentCommands: commands);

        foreach (var typed in new[] { "/", "/t", "/tr", "/tri", "/tria" })
        {
            service.GetSuggestions(typed);
        }

        commands.Reads.Should().Be(1);
        service.GetSuggestions("/tria").Should().Contain(command => command.Name == "/triage");
    }

    public void Dispose() => _folder.Dispose();

    private sealed class CountingCommands : IAgentCommandCatalog
    {
        public int Reads { get; private set; }

        public string UserCommandsDirectory => "commands";

        public IReadOnlyList<AgentCommandInfo> GetCommands()
        {
            Reads++;
            return [new AgentCommandInfo("triage", "Triage new issues", string.Empty, "triage.md", "user")];
        }

        public bool TryExpand(string input, out string prompt)
        {
            prompt = string.Empty;
            return false;
        }
    }

    private IAgentPluginCatalog PluginsWith(params string[] commandFiles)
    {
        var catalog = new Mock<IAgentPluginCatalog>();
        catalog.Setup(c => c.GetPlugins()).Returns(
        [
            new AgentPluginInfo { Name = "tools", Directory = _folder.Path("plugin"), Enabled = true, CommandFiles = commandFiles }
        ]);
        return catalog.Object;
    }
}
