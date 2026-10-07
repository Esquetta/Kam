using FluentAssertions;
using System.Text.Json.Nodes;
using SmartVoiceAgent.Infrastructure.Agent.Mcp;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Mcp;

public sealed class UserMcpServerSourceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kam-mcp-json-" + Guid.NewGuid().ToString("N"));

    private string ConfigPath => Path.Combine(_directory, "Kam", "mcp.json");

    [Fact]
    public void AddServer_WithoutFile_CreatesItUnderMcpServers()
    {
        var source = new UserMcpServerSource(ConfigPath);

        source.AddServer("fetch", new JsonObject { ["command"] = "uvx", ["args"] = new JsonArray("mcp-server-fetch") });

        var server = source.GetServers().Should().ContainSingle().Subject;
        server.Name.Should().Be("fetch");
        server.Command.Should().Be("uvx");
        server.Arguments.Should().Equal("mcp-server-fetch");
        JsonNode.Parse(File.ReadAllText(ConfigPath))!["mcpServers"]!["fetch"].Should().NotBeNull();
    }

    [Fact]
    public void AddServer_KeepsOtherEntriesAndSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, """
            {
              // Mine
              "theme": "dark",
              "mcpServers": {
                "github": { "type": "http", "url": "https://api.githubcopilot.com/mcp/", "headers": { "Authorization": "Bearer ${secret:GITHUB}" } },
              }
            }
            """);
        var source = new UserMcpServerSource(ConfigPath);

        source.AddServer("deepwiki", new JsonObject { ["type"] = "http", ["url"] = "https://mcp.deepwiki.com/mcp" });

        source.GetServers().Select(server => server.Name).Should().Equal("github", "deepwiki");
        source.GetServers()[0].Headers["Authorization"].Should().Be("Bearer ${secret:GITHUB}");
        JsonNode.Parse(File.ReadAllText(ConfigPath))!["theme"]!.GetValue<string>().Should().Be("dark");
    }

    [Fact]
    public void AddServer_FileListingServersAtRoot_AddsBesideThem()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, """{ "files": { "command": "npx", "args": ["-y", "server-filesystem"] } }""");
        var source = new UserMcpServerSource(ConfigPath);

        source.AddServer("time", new JsonObject { ["command"] = "uvx", ["args"] = new JsonArray("mcp-server-time") });

        source.GetServers().Select(server => server.Name).Should().Equal("files", "time");
        JsonNode.Parse(File.ReadAllText(ConfigPath))!.AsObject().ContainsKey("mcpServers").Should().BeFalse();
    }

    [Fact]
    public void SetServerEnabled_WritesTheDisabledFlag()
    {
        var source = new UserMcpServerSource(ConfigPath);
        source.AddServer("memory", new JsonObject { ["command"] = "npx", ["enabled"] = false });

        source.SetServerEnabled("memory", true).Should().BeTrue();
        source.GetServers().Single().Disabled.Should().BeFalse();

        source.SetServerEnabled("memory", false).Should().BeTrue();
        source.GetServers().Single().Disabled.Should().BeTrue();
        source.SetServerEnabled("missing", true).Should().BeFalse();
    }

    [Fact]
    public void RemoveServer_RemovesOnlyThatEntry()
    {
        var source = new UserMcpServerSource(ConfigPath);
        source.RemoveServer("fetch").Should().BeFalse();
        source.AddServer("fetch", new JsonObject { ["command"] = "uvx" });
        source.AddServer("time", new JsonObject { ["command"] = "uvx" });

        source.RemoveServer("fetch").Should().BeTrue();

        source.GetServers().Select(server => server.Name).Should().Equal("time");
    }

    [Fact]
    public void AddServer_InvalidFile_ThrowsWithoutChangingIt()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, "{ not json");
        var source = new UserMcpServerSource(ConfigPath);

        var add = () => source.AddServer("fetch", new JsonObject { ["command"] = "uvx" });

        add.Should().Throw<InvalidOperationException>().WithMessage("mcp.json is not valid JSON*");
        File.ReadAllText(ConfigPath).Should().Be("{ not json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
