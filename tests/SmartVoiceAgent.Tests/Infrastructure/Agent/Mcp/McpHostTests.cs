using System.IO.Pipelines;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.Agents.Extensions;
using SmartVoiceAgent.Infrastructure.Agent.Mcp;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Mcp;

public sealed class McpHostTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _servers = [];
    private readonly List<CancellationTokenSource> _serverStops = [];

    [Fact]
    public async Task GetToolsAsync_ConnectsServer_AndNamesToolsAfterIt()
    {
        await using var host = CreateHost([Stdio("notes")]);

        var tools = await host.GetToolsAsync();

        tools.Select(tool => tool.Name).Should().BeEquivalentTo("mcp__notes__echo", "mcp__notes__delete_note", "mcp__notes__fail");
        tools.Should().OnlyContain(tool => tool.Source == "mcp:notes");
        tools.Single(tool => tool.Name == "mcp__notes__echo").Risk.Should().Be(ToolRisk.Read);
        tools.Single(tool => tool.Name == "mcp__notes__delete_note").Risk.Should().Be(ToolRisk.External);
        host.Servers.Should().ContainSingle()
            .Which.Should().Match<McpServerState>(state => state.Status == McpServerStatus.Ready && state.ToolCount == 3);
    }

    [Fact]
    public async Task Tool_Invoke_CallsServerAndReturnsText()
    {
        await using var host = CreateHost([Stdio("notes")]);
        var echo = (await host.GetToolsAsync()).Single(tool => tool.Name == "mcp__notes__echo");

        var result = await echo.Function.InvokeAsync(new AIFunctionArguments { ["text"] = "hello" });

        result.Should().Be("echo: hello");
        echo.Function.JsonSchema.GetProperty("properties").TryGetProperty("text", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Tool_Invoke_ServerError_ReturnsErrorText()
    {
        await using var host = CreateHost([Stdio("notes")]);
        var fail = (await host.GetToolsAsync()).Single(tool => tool.Name == "mcp__notes__fail");

        var result = await fail.Function.InvokeAsync(new AIFunctionArguments());

        result.Should().BeOfType<string>().Which.Should().StartWith("Error:");
    }

    [Fact]
    public async Task GetToolsAsync_FailedServer_StaysFailedUntilRestart()
    {
        var attempts = 0;
        await using var host = new McpHost(
            [new FixedSource(Stdio("broken"), Stdio("notes"))],
            "mcp.json",
            transportFactory: definition =>
            {
                if (definition.Name == "broken")
                {
                    attempts++;
                    throw new InvalidOperationException("command not found");
                }

                return CreateServerTransport();
            });

        var tools = await host.GetToolsAsync();
        await host.GetToolsAsync();

        tools.Should().OnlyContain(tool => tool.Source == "mcp:notes");
        attempts.Should().Be(1);
        host.Servers.Single(state => state.Definition.Name == "broken")
            .Should().Match<McpServerState>(state => state.Status == McpServerStatus.Failed && state.Error == "command not found");

        await host.RestartAsync("broken");
        host.Servers.Single(state => state.Definition.Name == "broken").Status.Should().Be(McpServerStatus.Idle);
        await host.GetToolsAsync();

        attempts.Should().Be(2);
    }

    [Fact]
    public async Task GetToolsAsync_SlowServer_FailsAfterTimeout()
    {
        await using var host = new McpHost(
            [new FixedSource(Stdio("slow"))],
            "mcp.json",
            transportFactory: _ => new StreamClientTransport(new Pipe().Writer.AsStream(), new Pipe().Reader.AsStream()),
            connectTimeout: TimeSpan.FromMilliseconds(200));

        var tools = await host.GetToolsAsync();

        tools.Should().BeEmpty();
        host.Servers.Single().Should().Match<McpServerState>(state =>
            state.Status == McpServerStatus.Failed && state.Error!.Contains("Did not answer"));
    }

    [Fact]
    public async Task GetToolsAsync_SlowServer_DoesNotHoldUpTheTurn()
    {
        await using var host = new McpHost(
            [new FixedSource(Stdio("slow"))],
            "mcp.json",
            transportFactory: _ => new StreamClientTransport(new Pipe().Writer.AsStream(), new Pipe().Reader.AsStream()),
            connectTimeout: TimeSpan.FromSeconds(30),
            turnWait: TimeSpan.FromMilliseconds(100));

        var started = DateTime.UtcNow;
        var tools = await host.GetToolsAsync();

        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(10));
        tools.Should().BeEmpty();
        host.Servers.Single().Status.Should().Be(McpServerStatus.Connecting, "the server keeps starting for a later turn");
    }

    [Fact]
    public async Task GetToolsAsync_DisabledServer_IsNotStarted()
    {
        var started = new List<string>();
        await using var host = new McpHost(
            [new FixedSource(Stdio("off") with { Disabled = true })],
            "mcp.json",
            transportFactory: definition =>
            {
                started.Add(definition.Name);
                return CreateServerTransport();
            });

        (await host.GetToolsAsync()).Should().BeEmpty();

        started.Should().BeEmpty();
        host.Servers.Single().Status.Should().Be(McpServerStatus.Disabled);
    }

    [Fact]
    public async Task Servers_SameNameInTwoSources_FirstSourceWins()
    {
        await using var host = new McpHost(
            [new FixedSource(Stdio("github") with { Command = "user-cmd" }), new FixedSource(Stdio("github") with { Command = "plugin-cmd", Source = "plugin:x" })],
            "mcp.json",
            transportFactory: _ => CreateServerTransport());

        host.Servers.Should().ContainSingle().Which.Definition.Command.Should().Be("user-cmd");
    }

    [Fact]
    public async Task Connect_ResolvesReferences_SecretsOnlyForUserEntries()
    {
        var resolved = new List<McpServerDefinition>();
        await using var host = new McpHost(
            [new FixedSource(
                Stdio("mine") with { Environment = new Dictionary<string, string> { ["TOKEN"] = "${secret:GitHubToken}" } },
                Stdio("plugin") with { Source = "plugin:x", Environment = new Dictionary<string, string> { ["TOKEN"] = "${secret:GitHubToken}" } })],
            "mcp.json",
            new FakeSecrets(),
            transportFactory: definition =>
            {
                lock (resolved)
                {
                    resolved.Add(definition);
                }

                return CreateServerTransport();
            });

        await host.GetToolsAsync();

        resolved.Single(definition => definition.Name == "mine").Environment["TOKEN"].Should().Be("ghp_secret");
        resolved.Single(definition => definition.Name == "plugin").Environment["TOKEN"].Should().BeEmpty();
        host.Servers.Single(state => state.Definition.Name == "mine").Definition.Environment["TOKEN"]
            .Should().Be("${secret:GitHubToken}", "the resolved value is used only to start the server");
    }

    [Fact]
    public async Task ReloadAsync_DropsRemovedAndChangedServers_KeepsOthersConnected()
    {
        var source = new MutableSource(Stdio("a"), Stdio("b"));
        var started = new List<string>();
        await using var host = new McpHost(
            [source],
            "mcp.json",
            transportFactory: definition =>
            {
                lock (started)
                {
                    started.Add(definition.Name);
                }

                return CreateServerTransport();
            });
        await host.GetToolsAsync();

        source.Servers = [Stdio("a"), Stdio("b") with { Arguments = ["--changed"] }, Stdio("c")];
        await host.ReloadAsync();

        host.Servers.Single(state => state.Definition.Name == "a").Status.Should().Be(McpServerStatus.Ready);
        host.Servers.Single(state => state.Definition.Name == "b").Status.Should().Be(McpServerStatus.Idle);
        await host.GetToolsAsync();
        started.Should().BeEquivalentTo(["a", "b", "b", "c"]);

        source.Servers = [Stdio("a")];
        await host.ReloadAsync();
        host.Servers.Select(state => state.Definition.Name).Should().Equal("a");
    }

    [Fact]
    public async Task StateChanged_IsRaisedWhenServerConnects()
    {
        await using var host = CreateHost([Stdio("notes")]);
        var raised = 0;
        host.StateChanged += (_, _) => Interlocked.Increment(ref raised);

        await host.GetToolsAsync();

        raised.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task McpToolProvider_ReturnsHostTools()
    {
        await using var host = CreateHost([Stdio("notes")]);

        var tools = await new McpToolProvider(host).GetToolsAsync();

        tools.Should().HaveCount(3);
    }

    [Theory]
    [InlineData("github", "search_issues", "mcp__github__search_issues")]
    [InlineData("my server", "list.files", "mcp__my_server__list_files")]
    public void ToToolName_SanitizesNames(string server, string tool, string expected)
    {
        McpHost.ToToolName(server, tool).Should().Be(expected);
    }

    [Fact]
    public void ToToolName_LongName_IsShortenedWithStableHash()
    {
        var name = McpHost.ToToolName("a-very-long-server-name-for-testing", "and_an_even_longer_tool_name_that_overflows");

        name.Length.Should().Be(64);
        name.Should().StartWith("mcp__a-very-long-server-name");
        McpHost.ToToolName("a-very-long-server-name-for-testing", "and_an_even_longer_tool_name_that_overflows").Should().Be(name);
        McpHost.ToToolName("a-very-long-server-name-for-testing", "and_an_even_longer_tool_name_that_overflowz").Should().NotBe(name);
    }

    [Theory]
    [InlineData(true, false, ToolRisk.Read)]
    [InlineData(true, true, ToolRisk.Network)]
    [InlineData(false, false, ToolRisk.External)]
    [InlineData(null, null, ToolRisk.External)]
    public void ClassifyRisk_UsesReadOnlyAndOpenWorldHints(bool? readOnly, bool? openWorld, ToolRisk expected)
    {
        var annotations = readOnly is null ? null : new ToolAnnotations { ReadOnlyHint = readOnly, OpenWorldHint = openWorld };

        McpHost.ClassifyRisk(annotations).Should().Be(expected);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var stop in _serverStops)
        {
            await stop.CancelAsync();
        }

        foreach (var server in _servers)
        {
            await server.DisposeAsync();
        }
    }

    private McpHost CreateHost(IReadOnlyList<McpServerDefinition> servers) =>
        new([new FixedSource([.. servers])], "mcp.json", transportFactory: _ => CreateServerTransport());

    private static McpServerDefinition Stdio(string name) => new() { Name = name, Command = "node", Arguments = ["server.js"] };

    private IClientTransport CreateServerTransport()
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var tools = new McpServerPrimitiveCollection<McpServerTool>
        {
            McpServerTool.Create((string text) => $"echo: {text}", new McpServerToolCreateOptions { Name = "echo", ReadOnly = true }),
            McpServerTool.Create((string id) => $"deleted {id}", new McpServerToolCreateOptions { Name = "delete_note", Destructive = true }),
            McpServerTool.Create(string () => throw new InvalidOperationException("boom"), new McpServerToolCreateOptions { Name = "fail" })
        };

        var server = McpServer.Create(
            new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream(), "test", NullLoggerFactory.Instance),
            new McpServerOptions
            {
                ServerInfo = new Implementation { Name = "test", Version = "1.0.0" },
                ToolCollection = tools
            },
            NullLoggerFactory.Instance);
        var stop = new CancellationTokenSource();
        _ = server.RunAsync(stop.Token);
        lock (_servers)
        {
            _servers.Add(server);
            _serverStops.Add(stop);
        }

        return new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream(), NullLoggerFactory.Instance);
    }

    private sealed class FixedSource(params McpServerDefinition[] servers) : IMcpServerSource
    {
        public IReadOnlyList<McpServerDefinition> GetServers() => servers;
    }

    private sealed class MutableSource(params McpServerDefinition[] servers) : IMcpServerSource
    {
        public IReadOnlyList<McpServerDefinition> Servers { get; set; } = servers;

        public IReadOnlyList<McpServerDefinition> GetServers() => Servers;
    }

    private sealed class FakeSecrets : ISecretValueProvider
    {
        public string? GetSecret(string name) => name == "GitHubToken" ? "ghp_secret" : null;
    }
}
