using FluentAssertions;
using Moq;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.Agents.Extensions;
using SmartVoiceAgent.Infrastructure.Agent.Extensions;
using SmartVoiceAgent.Infrastructure.Agent.Mcp;
using SmartVoiceAgent.Ui.ViewModels.PageModels;
using System.Text.Json.Nodes;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class ExtensionsViewModelTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kam-ext-vm-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<IMcpHost> _host = new();
    private readonly Mock<IAgentPluginCatalog> _plugins = new();
    private readonly Mock<IAgentSkillCatalog> _skills = new();
    private readonly Mock<IAgentCommandCatalog> _commands = new();
    private readonly List<string> _opened = [];

    public ExtensionsViewModelTests()
    {
        _host.SetupGet(h => h.UserConfigPath).Returns(Path.Combine(_directory, "mcp.json"));
        _host.SetupGet(h => h.Servers).Returns(
        [
            new McpServerState(new McpServerDefinition { Name = "github", Transport = McpTransportKind.Http, Url = "https://api.githubcopilot.com/mcp/" }, McpServerStatus.Ready, 12, null),
            new McpServerState(new McpServerDefinition { Name = "files", Command = "npx", Arguments = ["-y", "server-filesystem"] }, McpServerStatus.Failed, 0, "command not found"),
            new McpServerState(new McpServerDefinition { Name = "db", Command = "uvx", Source = "plugin:db-tools" }, McpServerStatus.Idle, 0, null)
        ]);
        _host.Setup(h => h.ReloadAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _host.Setup(h => h.GetToolsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<AgentToolDescriptor>());
        _plugins.SetupGet(p => p.PluginsDirectory).Returns(Path.Combine(_directory, "plugins"));
        _plugins.Setup(p => p.GetPlugins()).Returns(
        [
            new AgentPluginInfo
            {
                Name = "git-tools",
                Version = "1.2.0",
                Author = "Ada",
                Directory = Path.Combine(_directory, "plugins", "git-tools"),
                Enabled = true,
                SkillDirectories = ["a", "b"],
                CommandFiles = ["c"],
                McpServers = [new McpServerDefinition { Name = "git", Command = "node" }]
            }
        ]);
        _skills.SetupGet(s => s.UserSkillsDirectory).Returns(Path.Combine(_directory, "skills"));
        _skills.Setup(s => s.GetSkills()).Returns(
        [
            new AgentSkillInfo("pdf", "Extract PDF text", Path.Combine(_directory, "skills", "pdf"), "user"),
            new AgentSkillInfo("git-tools:changelog", "Write a changelog", "plugin-dir", "plugin:git-tools")
        ]);
        _commands.SetupGet(c => c.UserCommandsDirectory).Returns(Path.Combine(_directory, "commands"));
        _commands.Setup(c => c.GetCommands()).Returns([new AgentCommandInfo("review", "Review the diff", "[file]", "review.md", "user")]);
    }

    [Fact]
    public void Constructor_ListsEveryKindOfExtension()
    {
        var viewModel = CreateViewModel();

        viewModel.SummaryText.Should().Be("3 MCP servers · 2 skills · 1 plugin · 1 command");
        viewModel.McpServers.Select(server => $"{server.Name}|{server.StatusText}|{server.Detail}|{server.SourceText}").Should().Equal(
            "github|12 tools|HTTP · api.githubcopilot.com/mcp|mcp.json",
            "files|Failed|stdio · npx -y server-filesystem|mcp.json",
            "db|Starts on first use|stdio · uvx|plugin:db-tools");
        viewModel.McpServers[0].IsReady.Should().BeTrue();
        viewModel.McpServers[1].IsFailed.Should().BeTrue();
        viewModel.McpServers[1].Error.Should().Be("command not found");
        var plugin = viewModel.Plugins.Single();
        plugin.VersionText.Should().Be("v1.2.0 · Ada");
        plugin.ComponentsText.Should().Be("2 skills · 1 command · 1 MCP server");
        viewModel.Skills.Select(skill => $"{skill.Name}|{skill.SourceText}|{skill.CanUninstall}").Should().Equal(
            "pdf|Your skills|True",
            "git-tools:changelog|Plugin git-tools|False");
        viewModel.Commands.Single().Usage.Should().Be("/review [file]");
        viewModel.HasNoMcpServers.Should().BeFalse();
    }

    [Fact]
    public async Task InstallPluginAsync_Success_ReloadsServersAndClearsInput()
    {
        _plugins.Setup(p => p.InstallAsync("acme/git-tools", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentPluginInstallResult(true, "Installed git-tools.", ["git-tools"]));
        var viewModel = CreateViewModel();
        viewModel.PluginSource = " acme/git-tools ";

        await viewModel.InstallPluginAsync();

        viewModel.Message.Should().Be("Installed git-tools.");
        viewModel.PluginSource.Should().BeEmpty();
        _host.Verify(h => h.ReloadAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InstallPluginAsync_Failure_KeepsInputAndShowsMessage()
    {
        _plugins.Setup(p => p.InstallAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentPluginInstallResult(false, "git clone failed: not found", []));
        var viewModel = CreateViewModel();
        viewModel.PluginSource = "acme/missing";

        await viewModel.InstallPluginAsync();

        viewModel.Message.Should().Be("git clone failed: not found");
        viewModel.PluginSource.Should().Be("acme/missing");
        _host.Verify(h => h.ReloadAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void TogglePlugin_SetsEnabledAndReloadsServers()
    {
        var viewModel = CreateViewModel();

        viewModel.Plugins.Single().IsEnabled = false;

        _plugins.Verify(p => p.SetEnabled("git-tools", false), Times.Once);
        _host.Verify(h => h.ReloadAsync(It.IsAny<CancellationToken>()), Times.Once);
        viewModel.Message.Should().Be("Turned off git-tools.");
    }

    [Fact]
    public void InstallSkill_ShowsResultOrError()
    {
        _skills.Setup(s => s.Install("C:/downloads/pdf")).Returns(new AgentSkillInfo("pdf", "d", "dir", "user"));
        _skills.Setup(s => s.Install("C:/empty")).Throws(new InvalidOperationException("No SKILL.md was found in C:/empty."));
        var viewModel = CreateViewModel();

        viewModel.SkillSource = "\"C:/downloads/pdf\"";
        viewModel.InstallSkill();
        viewModel.Message.Should().Be("Installed the pdf skill. The agent can use it from the next message.");
        viewModel.SkillSource.Should().BeEmpty();

        viewModel.SkillSource = "C:/empty";
        viewModel.InstallSkill();
        viewModel.Message.Should().Be("No SKILL.md was found in C:/empty.");
    }

    [Fact]
    public async Task ConnectServersAsync_ReportsToolsAndFailures()
    {
        _host.Setup(h => h.GetToolsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<AgentToolDescriptor>());
        var viewModel = CreateViewModel();

        await viewModel.ConnectServersAsync();

        viewModel.Message.Should().Be("0 tools available. 1 server failed to start; see the error below it.");
    }

    [Fact]
    public void OpenMcpConfig_CreatesTemplateAndOpensIt()
    {
        var viewModel = CreateViewModel();

        viewModel.OpenMcpConfigCommand.Execute(null);

        var path = Path.Combine(_directory, "mcp.json");
        File.ReadAllText(path).Should().Contain("mcpServers");
        _opened.Should().Equal(path);
    }

    [Fact]
    public void StateChanged_RefreshesServers()
    {
        var viewModel = CreateViewModel();
        _host.SetupGet(h => h.Servers).Returns([]);

        _host.Raise(h => h.StateChanged += null, EventArgs.Empty);

        viewModel.McpServers.Should().BeEmpty();
        viewModel.HasNoMcpServers.Should().BeTrue();
    }

    [Fact]
    public void Search_HidesRowsAndGroupsThatDoNotMatch()
    {
        var viewModel = CreateViewModel();

        viewModel.SearchText = "git";

        viewModel.McpServers.Where(server => server.IsVisible).Select(server => server.Name).Should().Equal("github");
        viewModel.Plugins.Single().IsVisible.Should().BeTrue();
        viewModel.Skills.Where(skill => skill.IsVisible).Select(skill => skill.Name).Should().Equal("git-tools:changelog");
        viewModel.VisibleMcpServerCount.Should().Be(1);
        viewModel.McpServerCount.Should().Be(3);
        viewModel.ShowCommandsSection.Should().BeFalse();
        viewModel.CatalogServers.Where(item => item.IsVisible).Select(item => item.Entry.Name).Should().Equal("git", "deepwiki");

        viewModel.SearchText = "zzz";

        viewModel.HasNoInstalledMatches.Should().BeTrue();
        viewModel.IsSearchWithoutMatches.Should().BeTrue();
        viewModel.IsNothingInstalled.Should().BeFalse();
        viewModel.NoMatchesText.Should().Be("Nothing matches “zzz”.");
        viewModel.HasNoCatalogServerMatches.Should().BeTrue();
    }

    [Fact]
    public void Filter_ShowsOneKind_EvenWhenItIsEmpty()
    {
        _commands.Setup(c => c.GetCommands()).Returns([]);
        var viewModel = CreateViewModel();

        viewModel.ShowCommandsSection.Should().BeFalse();
        viewModel.SetFilterCommand.Execute("Commands").Subscribe();

        viewModel.IsFilterCommands.Should().BeTrue();
        viewModel.ShowCommandsSection.Should().BeTrue();
        viewModel.HasNoCommands.Should().BeTrue();
        viewModel.ShowMcpSection.Should().BeFalse();
        viewModel.HasNoInstalledMatches.Should().BeFalse();

        viewModel.SetFilterCommand.Execute("Plugins").Subscribe();

        viewModel.ShowPluginsSection.Should().BeTrue();
        viewModel.ShowCommandsSection.Should().BeFalse();
    }

    [Fact]
    public void Tabs_SwitchBetweenInstalledAndDiscover()
    {
        var viewModel = CreateViewModel();

        viewModel.IsInstalledTab.Should().BeTrue();
        viewModel.ShowDiscoverCommand.Execute(null);
        viewModel.IsDiscoverTab.Should().BeTrue();
        viewModel.IsInstalledTab.Should().BeFalse();
        viewModel.ShowInstalledCommand.Execute(null);
        viewModel.IsInstalledTab.Should().BeTrue();
    }

    [Fact]
    public async Task AddCatalogServerAsync_WritesMcpJsonThenReloadsAndStarts()
    {
        var viewModel = CreateViewModel();
        var fetch = viewModel.CatalogServers.Single(item => item.Entry.Name == "fetch");

        await viewModel.AddCatalogServerAsync(fetch);

        var server = new UserMcpServerSource(Path.Combine(_directory, "mcp.json")).GetServers().Single();
        server.Name.Should().Be("fetch");
        server.Command.Should().Be("uvx");
        server.Arguments.Should().Equal("mcp-server-fetch");
        _host.Verify(h => h.ReloadAsync(It.IsAny<CancellationToken>()), Times.Once);
        _host.Verify(h => h.GetToolsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddCatalogServerAsync_NeedsFolder_AsksForOneAndStopsWhenCancelled()
    {
        var viewModel = CreateViewModel();
        var git = viewModel.CatalogServers.Single(item => item.Entry.Name == "git");
        var titles = new List<string>();
        string? answer = null;
        viewModel.PickFolderAsync = title =>
        {
            titles.Add(title);
            return Task.FromResult(answer);
        };

        await viewModel.AddCatalogServerAsync(git);

        File.Exists(Path.Combine(_directory, "mcp.json")).Should().BeFalse();
        _host.Verify(h => h.ReloadAsync(It.IsAny<CancellationToken>()), Times.Never);

        answer = "C:/src/kam";
        await viewModel.AddCatalogServerAsync(git);

        titles.Should().Equal("Choose a folder for Git", "Choose a folder for Git");
        new UserMcpServerSource(Path.Combine(_directory, "mcp.json")).GetServers().Single().Arguments
            .Should().Equal("mcp-server-git", "--repository", "C:/src/kam");
    }

    [Fact]
    public void CatalogCards_KnowWhatIsAlreadyInstalledAndWhatIsMissing()
    {
        _host.SetupGet(h => h.Servers).Returns(
        [
            new McpServerState(new McpServerDefinition { Name = "deepwiki", Transport = McpTransportKind.Http, Url = "https://mcp.deepwiki.com/mcp" }, McpServerStatus.Ready, 3, null),
            new McpServerState(new McpServerDefinition { Name = "fetch", Command = "uvx", Source = "plugin:web" }, McpServerStatus.Idle, 0, null)
        ]);
        _plugins.Setup(p => p.GetPlugins()).Returns([new AgentPluginInfo { Name = "document-skills", Directory = "d" }]);
        var viewModel = new ExtensionsViewModel(
            _host.Object, _plugins.Object, _skills.Object, _commands.Object, _opened.Add, action => action(),
            runtime => runtime != ExtensionRuntime.Uv);

        viewModel.CatalogServers.Where(item => item.IsAdded).Select(item => item.Entry.Name).Should().Equal("deepwiki");
        viewModel.CatalogPlugins.Single(item => item.Entry.Id == "anthropic-skills").IsAdded.Should().BeTrue();
        var fetch = viewModel.CatalogServers.Single(item => item.Entry.Name == "fetch");
        fetch.CanAdd.Should().BeTrue();
        fetch.IsRuntimeMissing.Should().BeTrue();
        fetch.MissingText.Should().Be("uv not found");
        fetch.GetRuntimeText.Should().Be("Get uv");
        fetch.Description.Should().Be("Reads web pages and turns them into Markdown the agent can use.");
        viewModel.CatalogServers.Single(item => item.Entry.Name == "memory").RuntimeText.Should().Be("Node.js");

        fetch.GetRuntimeCommand.Execute(null);
        fetch.OpenHomepageCommand.Execute(null);

        _opened.Should().Equal(
            "https://docs.astral.sh/uv/getting-started/installation/",
            "https://github.com/modelcontextprotocol/servers/tree/main/src/fetch");
    }

    [Fact]
    public async Task InstallCatalogPluginAsync_InstallsTheRepository()
    {
        _plugins.Setup(p => p.InstallAsync("anthropics/skills", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentPluginInstallResult(true, "Added 4 plugins from the marketplace, turned off.", ["document-skills"]));
        var viewModel = CreateViewModel();
        var card = viewModel.CatalogPlugins.Single(item => item.Entry.Id == "anthropic-skills");

        await viewModel.InstallCatalogPluginAsync(card);

        viewModel.Message.Should().Be("Added 4 plugins from the marketplace, turned off.");
        card.IsInstalling.Should().BeFalse();
        _host.Verify(h => h.ReloadAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void UserServerToggleAndRemove_EditMcpJson()
    {
        var config = new UserMcpServerSource(Path.Combine(_directory, "mcp.json"));
        config.AddServer("github", new JsonObject { ["type"] = "http", ["url"] = "https://api.githubcopilot.com/mcp/" });
        config.AddServer("files", new JsonObject { ["command"] = "npx" });
        var viewModel = CreateViewModel();
        var github = viewModel.McpServers[0];
        var files = viewModel.McpServers[1];

        github.CanToggle.Should().BeTrue();
        github.CanRemove.Should().BeTrue();
        viewModel.McpServers.Single(server => server.Name == "db").CanToggle.Should().BeFalse();

        github.IsEnabled = false;

        config.GetServers().Single(server => server.Name == "github").Disabled.Should().BeTrue();
        viewModel.Message.Should().Be("Turned off github.");

        // Reloading refreshes the list in the background, so use the row read before the toggle.
        files.RemoveCommand.Execute(null);

        config.GetServers().Select(server => server.Name).Should().Equal("github");
        viewModel.Message.Should().Be("Removed files from mcp.json.");
        _host.Verify(h => h.ReloadAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ConnectServersAsync_ReportsServersStillStarting()
    {
        _host.SetupGet(h => h.Servers).Returns(
        [
            new McpServerState(new McpServerDefinition { Name = "playwright", Command = "npx" }, McpServerStatus.Connecting, 0, null)
        ]);
        var viewModel = CreateViewModel();

        await viewModel.ConnectServersAsync();

        viewModel.Message.Should().Be("0 tools available. 1 server still starting; it shows here when ready.");
        _host.Verify(h => h.ReloadAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private ExtensionsViewModel CreateViewModel() =>
        new(_host.Object, _plugins.Object, _skills.Object, _commands.Object, _opened.Add, action => action(), _ => true);
}
