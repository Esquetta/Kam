using FluentAssertions;
using ModelContextProtocol.Protocol;
using SmartVoiceAgent.Core.Models.Agents.Extensions;
using SmartVoiceAgent.Infrastructure.Agent.Mcp;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Mcp;

public class McpConfigParserTests
{
    [Fact]
    public void Parse_ClaudeDesktopFormat_ReadsStdioAndHttpServers()
    {
        const string json = """
            {
              // Copied from Claude Desktop
              "mcpServers": {
                "filesystem": {
                  "command": "npx",
                  "args": ["-y", "@modelcontextprotocol/server-filesystem", "C:/Users/me/Documents"],
                  "env": { "DEBUG": "1" },
                  "cwd": "C:/work"
                },
                "github": {
                  "type": "http",
                  "url": "https://api.githubcopilot.com/mcp/",
                  "headers": { "Authorization": "Bearer ${secret:GitHubToken}" }
                },
              }
            }
            """;

        var (servers, errors) = McpConfigParser.Parse(json, "user");

        errors.Should().BeEmpty();
        servers.Should().HaveCount(2);
        var filesystem = servers.Single(server => server.Name == "filesystem");
        filesystem.Transport.Should().Be(McpTransportKind.Stdio);
        filesystem.Command.Should().Be("npx");
        filesystem.Arguments.Should().Equal("-y", "@modelcontextprotocol/server-filesystem", "C:/Users/me/Documents");
        filesystem.Environment.Should().ContainKey("DEBUG").WhoseValue.Should().Be("1");
        filesystem.WorkingDirectory.Should().Be("C:/work");
        var github = servers.Single(server => server.Name == "github");
        github.Transport.Should().Be(McpTransportKind.Http);
        github.Url.Should().Be("https://api.githubcopilot.com/mcp/");
        github.Headers["Authorization"].Should().Be("Bearer ${secret:GitHubToken}", "references resolve when the server starts");
        servers.Should().OnlyContain(server => server.Source == "user");
    }

    [Fact]
    public void Parse_PluginFormat_ServersAtRoot_SubstitutesPluginRoot()
    {
        const string json = """
            { "db": { "command": "${CLAUDE_PLUGIN_ROOT}/bin/db", "args": ["--config", "${CLAUDE_PLUGIN_ROOT}/db.json"] } }
            """;

        var (servers, _) = McpConfigParser.Parse(
            json,
            "plugin:db-tools",
            new Dictionary<string, string> { ["CLAUDE_PLUGIN_ROOT"] = "/plugins/db-tools" });

        var server = servers.Should().ContainSingle().Subject;
        server.Command.Should().Be("/plugins/db-tools/bin/db");
        server.Arguments.Should().Equal("--config", "/plugins/db-tools/db.json");
        server.Source.Should().Be("plugin:db-tools");
    }

    [Theory]
    [InlineData("""{ "s": { "url": "https://example.com/mcp" } }""", McpTransportKind.Http)]
    [InlineData("""{ "s": { "type": "sse", "url": "https://example.com/sse" } }""", McpTransportKind.Http)]
    [InlineData("""{ "s": { "type": "stdio", "command": "uvx", "args": ["mcp-server-git"] } }""", McpTransportKind.Stdio)]
    public void Parse_DetectsTransport(string json, McpTransportKind expected)
    {
        McpConfigParser.Parse(json, "user").Servers.Single().Transport.Should().Be(expected);
    }

    [Theory]
    [InlineData("""{ "mcpServers": { "s": { "command": "x", "disabled": true } } }""")]
    [InlineData("""{ "mcpServers": { "s": { "command": "x", "enabled": false } } }""")]
    public void Parse_DisabledEntries_AreKeptButMarked(string json)
    {
        McpConfigParser.Parse(json, "user").Servers.Single().Disabled.Should().BeTrue();
    }

    [Fact]
    public void Parse_InvalidEntries_AreSkippedWithMessage()
    {
        const string json = """
            { "mcpServers": { "good": { "command": "x" }, "nothing": { "args": [] }, "web": { "type": "http" } } }
            """;

        var (servers, errors) = McpConfigParser.Parse(json, "user");

        servers.Select(server => server.Name).Should().Equal("good");
        errors.Should().HaveCount(2);
        errors.Should().Contain(error => error.Contains("'nothing'"));
        errors.Should().Contain(error => error.Contains("'web'") && error.Contains("url"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    public void Parse_BadDocument_ReturnsError(string json)
    {
        var (servers, errors) = McpConfigParser.Parse(json, "user");

        servers.Should().BeEmpty();
        errors.Should().ContainSingle();
    }

    [Fact]
    public void Format_JoinsTextAndDescribesOtherBlocks()
    {
        var result = new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = "first" },
                new ImageContentBlock { Data = "aGk="u8.ToArray(), MimeType = "image/png" },
                new TextContentBlock { Text = "second" }
            ]
        };

        McpResultFormatter.Format(result).Should().Be("first\n[image image/png]\nsecond");
    }

    [Fact]
    public void Format_Error_StartsWithError()
    {
        var result = new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = "not allowed" }] };

        McpResultFormatter.Format(result).Should().Be("Error: not allowed");
    }

    [Fact]
    public void Format_Empty_ReturnsDone()
    {
        McpResultFormatter.Format(new CallToolResult { Content = [] }).Should().Be("Done.");
    }
}
