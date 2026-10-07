using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Agent.Extensions;
using SmartVoiceAgent.Infrastructure.Agent.Mcp;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Extensions;

public sealed class ExtensionCatalogTests : IDisposable
{
    private readonly ExtensionTestFolder _folder = new();

    [Fact]
    public void McpServers_HaveUniqueNamesThatMakeValidToolNames()
    {
        var names = ExtensionCatalog.McpServers.Select(entry => entry.Name).ToList();

        names.Should().OnlyHaveUniqueItems();
        names.Should().OnlyContain(name => McpHost.ToToolName(name, "tool") == $"mcp__{name}__tool");
        ExtensionCatalog.McpServers.Should().OnlyContain(entry => entry.Homepage.StartsWith("https://", StringComparison.Ordinal));
    }

    [Fact]
    public void ToConfigEntry_WritesTheCommandForEachRuntime()
    {
        Entry("fetch").ToConfigEntry().ToJsonString().Should().Be("""{"command":"uvx","args":["mcp-server-fetch"]}""");
        Entry("memory").ToConfigEntry().ToJsonString().Should().Be("""{"command":"npx","args":["-y","@modelcontextprotocol/server-memory"]}""");
        Entry("deepwiki").ToConfigEntry().ToJsonString().Should().Be("""{"type":"http","url":"https://mcp.deepwiki.com/mcp"}""");
    }

    [Fact]
    public void ToConfigEntry_PutsThePickedFolderInPlace()
    {
        var git = Entry("git");

        git.NeedsFolder.Should().BeTrue();
        git.ToConfigEntry("C:/src/kam").ToJsonString().Should().Be("""{"command":"uvx","args":["mcp-server-git","--repository","C:/src/kam"]}""");
        var withoutFolder = () => git.ToConfigEntry();
        withoutFolder.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CommandLocator_OnWindows_TriesPathExtensions()
    {
        _folder.Write("node/npx", "#!/bin/sh");
        _folder.Write("node/npx.cmd", "@echo off");
        var path = $"{_folder.Path("empty")};\"{_folder.Path("node")}\"";

        CommandLocator.Find("npx", path, ".EXE;.cmd", windows: true).Should().Be(_folder.Path("node", "npx.cmd"));
        CommandLocator.Find("uvx", path, ".EXE;.cmd", windows: true).Should().BeNull();
    }

    [Fact]
    public void CommandLocator_OnUnix_FindsTheProgramItself()
    {
        _folder.Write("bin/uvx", "#!/bin/sh");

        CommandLocator.Find("uvx", _folder.Path("bin"), windows: false).Should().Be(_folder.Path("bin", "uvx"));
        CommandLocator.Find("npx", _folder.Path("bin"), windows: false).Should().BeNull();
    }

    public void Dispose() => _folder.Dispose();

    private static McpCatalogEntry Entry(string name) => ExtensionCatalog.McpServers.Single(entry => entry.Name == name);
}
