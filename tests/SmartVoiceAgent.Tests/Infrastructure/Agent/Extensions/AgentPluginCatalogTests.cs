using FluentAssertions;
using SmartVoiceAgent.Core.Models.Agents.Extensions;
using SmartVoiceAgent.Infrastructure.Agent.Extensions;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Extensions;

public sealed class AgentPluginCatalogTests : IDisposable
{
    private readonly ExtensionTestFolder _folder = new();

    [Fact]
    public void ReadPlugin_ClaudeCodeLayout_ReadsManifestAndComponents()
    {
        WritePlugin("src/git-tools", "git-tools");

        var plugin = AgentPluginCatalog.ReadPlugin(_folder.Path("src", "git-tools"));

        plugin.Name.Should().Be("git-tools");
        plugin.Version.Should().Be("1.2.0");
        plugin.Description.Should().Be("Git helpers");
        plugin.Author.Should().Be("Ada");
        plugin.SkillDirectories.Select(Path.GetFileName).Should().Equal("changelog");
        plugin.CommandFiles.Select(Path.GetFileName).Should().Equal("pr.md");
        plugin.AgentCount.Should().Be(1);
        var server = plugin.McpServers.Should().ContainSingle().Subject;
        server.Name.Should().Be("git");
        server.Source.Should().Be("plugin:git-tools");
        server.Arguments.Should().Equal(Path.Combine(_folder.Path("src", "git-tools")) + "/server.js");
        plugin.Error.Should().BeNull();
    }

    [Fact]
    public void ReadPlugin_ManifestPathsOutsidePlugin_AreIgnored()
    {
        _folder.Write("outside/skills/evil/SKILL.md", "---\nname: evil\n---\n");
        _folder.Write("src/p/.claude-plugin/plugin.json", """{ "name": "p", "skills": ["../outside/skills", "/etc"], "commands": "./extra" }""");
        _folder.Write("src/p/extra/hello.md", "Say hello");

        var plugin = AgentPluginCatalog.ReadPlugin(_folder.Path("src", "p"));

        plugin.SkillDirectories.Should().BeEmpty();
        plugin.CommandFiles.Select(Path.GetFileName).Should().Equal("hello.md");
    }

    [Fact]
    public void ReadPlugin_InvalidManifest_IsListedWithError()
    {
        _folder.Write("src/broken/.claude-plugin/plugin.json", "{ not json");

        var plugin = AgentPluginCatalog.ReadPlugin(_folder.Path("src", "broken"));

        plugin.Name.Should().Be("broken");
        plugin.Error.Should().Contain("not valid JSON");
    }

    [Fact]
    public async Task InstallAsync_FromFolder_CopiesAndEnables()
    {
        WritePlugin("src/git-tools", "git-tools");
        var catalog = CreateCatalog();
        var changed = 0;
        catalog.Changed += (_, _) => changed++;

        var result = await catalog.InstallAsync(_folder.Path("src", "git-tools"));

        result.Success.Should().BeTrue();
        result.Installed.Should().Equal("git-tools");
        changed.Should().Be(1);
        var plugin = catalog.GetPlugins().Should().ContainSingle().Subject;
        plugin.Enabled.Should().BeTrue();
        plugin.Directory.Should().Be(_folder.Path("plugins", "git-tools"));
        Directory.Exists(_folder.Path("plugins", "git-tools", ".git")).Should().BeFalse();
    }

    [Fact]
    public async Task InstallAsync_FolderWithNothingInside_Fails()
    {
        _folder.Write("src/empty/readme.txt", "hi");

        var result = await CreateCatalog().InstallAsync(_folder.Path("src", "empty"));

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("No plugin was found");
    }

    [Fact]
    public async Task InstallAsync_Marketplace_InstallsLocalPluginsTurnedOff()
    {
        WritePlugin("market/plugins/git-tools", "git-tools");
        WritePlugin("market/plugins/docs", "docs");
        _folder.Write("market/.claude-plugin/marketplace.json", """
            {
              "name": "team",
              "plugins": [
                { "name": "git-tools", "source": "./plugins/git-tools" },
                { "name": "docs", "source": "./plugins/docs" },
                { "name": "remote", "source": { "source": "github", "repo": "someone/else" } },
                { "name": "escape", "source": "../../etc" }
              ]
            }
            """);
        var catalog = CreateCatalog();

        var result = await catalog.InstallAsync(_folder.Path("market"));

        result.Success.Should().BeTrue();
        result.Installed.Should().Equal("git-tools", "docs");
        result.Message.Should().Contain("turned off").And.Contain("Skipped 2");
        catalog.GetPlugins().Should().HaveCount(2).And.OnlyContain(plugin => !plugin.Enabled);
    }

    [Fact]
    public async Task InstallAsync_GitUrl_ClonesThenInstalls()
    {
        string? clonedUrl = null;
        var catalog = CreateCatalog((url, destination, _) =>
        {
            clonedUrl = url;
            WritePluginAt(destination, "git-tools");
            return Task.FromResult((0, string.Empty));
        });

        var result = await catalog.InstallAsync("acme/git-tools");

        clonedUrl.Should().Be("https://github.com/acme/git-tools.git");
        result.Installed.Should().Equal("git-tools");
    }

    [Theory]
    [InlineData("http://example.com/repo.git")]
    [InlineData("--upload-pack=evil")]
    [InlineData("https://user:pass@example.com/repo.git")]
    [InlineData("file:///etc")]
    public async Task InstallAsync_UnsafeSources_AreRejectedWithoutCloning(string source)
    {
        var cloned = false;
        var catalog = CreateCatalog((_, _, _) =>
        {
            cloned = true;
            return Task.FromResult((0, string.Empty));
        });

        var result = await catalog.InstallAsync(source);

        result.Success.Should().BeFalse();
        cloned.Should().BeFalse();
    }

    [Fact]
    public async Task InstallAsync_CloneFails_ReportsGitOutput()
    {
        var catalog = CreateCatalog((_, _, _) => Task.FromResult((128, "fatal: repository not found")));

        var result = await catalog.InstallAsync("https://github.com/acme/missing.git");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("repository not found");
    }

    [Fact]
    public async Task SetEnabled_PersistsAcrossInstances()
    {
        WritePlugin("src/git-tools", "git-tools");
        await CreateCatalog().InstallAsync(_folder.Path("src", "git-tools"));

        CreateCatalog().SetEnabled("git-tools", false);

        CreateCatalog().GetPlugins().Single().Enabled.Should().BeFalse();
        CreateCatalog().SetEnabled("git-tools", true);
        CreateCatalog().GetPlugins().Single().Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Uninstall_DeletesPlugin()
    {
        WritePlugin("src/git-tools", "git-tools");
        var catalog = CreateCatalog();
        await catalog.InstallAsync(_folder.Path("src", "git-tools"));

        catalog.Uninstall("git-tools").Should().BeTrue();

        catalog.GetPlugins().Should().BeEmpty();
        Directory.Exists(_folder.Path("plugins", "git-tools")).Should().BeFalse();
    }

    [Fact]
    public async Task PluginMcpServerSource_OnlyEnabledPlugins()
    {
        WritePlugin("src/git-tools", "git-tools");
        var catalog = CreateCatalog();
        await catalog.InstallAsync(_folder.Path("src", "git-tools"));
        var source = new PluginMcpServerSource(catalog);

        source.GetServers().Select(server => server.Name).Should().Equal("git");
        catalog.SetEnabled("git-tools", false);
        source.GetServers().Should().BeEmpty();
    }

    public void Dispose() => _folder.Dispose();

    private AgentPluginCatalog CreateCatalog(Func<string, string, CancellationToken, Task<(int, string)>>? gitClone = null) =>
        new(_folder.Path("plugins"), _folder.Path("plugins.json"), gitClone: gitClone);

    private void WritePlugin(string relative, string name) => WritePluginAt(_folder.Path(relative.Split('/')), name);

    private static void WritePluginAt(string directory, string name)
    {
        void Write(string relative, string content)
        {
            var path = Path.Combine(directory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        Write(".claude-plugin/plugin.json", $$"""
            { "name": "{{name}}", "version": "1.2.0", "description": "Git helpers", "author": { "name": "Ada" } }
            """);
        Write("skills/changelog/SKILL.md", "---\nname: changelog\ndescription: Write a changelog\n---\nBody");
        Write("commands/pr.md", "---\ndescription: Open a PR\n---\nOpen a pull request for $ARGUMENTS");
        Write("agents/reviewer.md", "---\nname: reviewer\n---\nReview code.");
        Write(".mcp.json", """{ "mcpServers": { "git": { "command": "node", "args": ["${CLAUDE_PLUGIN_ROOT}/server.js"] } } }""");
        Write(".git/HEAD", "ref: refs/heads/main");
    }
}
