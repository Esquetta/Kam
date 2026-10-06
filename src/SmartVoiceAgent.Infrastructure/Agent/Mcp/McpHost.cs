using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.Agents.Extensions;

namespace SmartVoiceAgent.Infrastructure.Agent.Mcp;

/// <summary>
/// Starts configured MCP servers (at app start, or on the first agent turn that needs tools) and offers their
/// tools as <c>mcp__{server}__{tool}</c>. A turn waits only briefly for servers that are still starting; their
/// tools join a later turn. A failed server stays failed until it is restarted or the configuration reloads.
/// </summary>
public sealed class McpHost : IMcpHost, IAsyncDisposable, IDisposable
{
    private const int MaxToolNameLength = 64;
    private static readonly Regex UnsafeNameCharacters = new("[^A-Za-z0-9_-]", RegexOptions.Compiled);

    private readonly IReadOnlyList<IMcpServerSource> _sources;
    private readonly ISecretValueProvider? _secrets;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly Func<McpServerDefinition, IClientTransport> _transportFactory;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _turnWait;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<string, ServerEntry> _servers = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    /// <summary>
    /// Creates the host.
    /// </summary>
    /// <param name="sources">Where server entries come from; when two use the same name, the first wins.</param>
    /// <param name="userConfigPath">The user's <c>mcp.json</c>.</param>
    /// <param name="secrets">Resolves <c>${secret:NAME}</c> references.</param>
    /// <param name="loggerFactory">Logger factory, also handed to the MCP client.</param>
    /// <param name="transportFactory">Creates the connection to a server whose references are already resolved;
    /// tests replace it.</param>
    /// <param name="connectTimeout">How long a server may take to start and list its tools.</param>
    /// <param name="turnWait">How long <see cref="GetToolsAsync"/> waits for servers that are still starting.</param>
    public McpHost(
        IEnumerable<IMcpServerSource> sources,
        string userConfigPath,
        ISecretValueProvider? secrets = null,
        ILoggerFactory? loggerFactory = null,
        Func<McpServerDefinition, IClientTransport>? transportFactory = null,
        TimeSpan? connectTimeout = null,
        TimeSpan? turnWait = null)
    {
        _sources = sources.ToList();
        UserConfigPath = userConfigPath;
        _secrets = secrets;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<McpHost>();
        _transportFactory = transportFactory ?? CreateTransport;
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(30);
        _turnWait = turnWait ?? TimeSpan.FromSeconds(5);
    }

    /// <inheritdoc />
    public string UserConfigPath { get; }

    /// <inheritdoc />
    public event EventHandler? StateChanged;

    /// <inheritdoc />
    public IReadOnlyList<McpServerState> Servers
    {
        get
        {
            EnsureLoaded();
            lock (_gate)
            {
                return _servers.Values
                    .Select(entry => new McpServerState(entry.Definition, entry.Status, entry.Tools.Count, entry.Error))
                    .ToList();
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentToolDescriptor>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        EnsureLoaded();

        List<Task> pending;
        lock (_gate)
        {
            foreach (var entry in _servers.Values.Where(entry => entry.Status == McpServerStatus.Idle))
            {
                entry.Status = McpServerStatus.Connecting;
                entry.Connecting = ConnectAsync(entry);
            }

            pending = _servers.Values
                .Where(entry => entry.Connecting is not null)
                .Select(entry => entry.Connecting!)
                .ToList();
        }

        if (pending.Count > 0)
        {
            RaiseStateChanged();
            // Connecting runs on its own clock, so a stopped turn does not leave a server half-started,
            // and a slow server keeps starting after the turn stops waiting for it.
            try
            {
                await Task.WhenAll(pending).WaitAsync(_turnWait, cancellationToken);
            }
            catch (TimeoutException)
            {
                _logger.LogInformation("Continuing without MCP servers that are still starting");
            }
        }

        lock (_gate)
        {
            return _servers.Values
                .Where(entry => entry.Status == McpServerStatus.Ready)
                .SelectMany(entry => entry.Tools)
                .ToList();
        }
    }

    /// <inheritdoc />
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        var definitions = ReadDefinitions();
        var toDispose = new List<McpClient>();

        lock (_gate)
        {
            foreach (var name in _servers.Keys.ToList())
            {
                var entry = _servers[name];
                if (!definitions.TryGetValue(name, out var definition) || Fingerprint(definition) != Fingerprint(entry.Definition))
                {
                    if (entry.Client is not null)
                    {
                        toDispose.Add(entry.Client);
                    }

                    _servers.Remove(name);
                }
            }

            foreach (var definition in definitions.Values)
            {
                if (!_servers.ContainsKey(definition.Name))
                {
                    _servers[definition.Name] = new ServerEntry(definition);
                }
            }

            _loaded = true;
        }

        foreach (var client in toDispose)
        {
            await DisposeClientAsync(client);
        }

        RaiseStateChanged();
    }

    /// <inheritdoc />
    public async Task RestartAsync(string serverName)
    {
        McpClient? client = null;
        lock (_gate)
        {
            if (!_servers.TryGetValue(serverName, out var entry) || entry.Status == McpServerStatus.Disabled)
            {
                return;
            }

            client = entry.Client;
            _servers[serverName] = new ServerEntry(entry.Definition);
        }

        if (client is not null)
        {
            await DisposeClientAsync(client);
        }

        RaiseStateChanged();
    }

    /// <summary>
    /// Builds the model-facing name of an MCP tool: <c>mcp__{server}__{tool}</c>, limited to the characters
    /// and length providers accept.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <param name="toolName">The tool name the server reports.</param>
    public static string ToToolName(string serverName, string toolName)
    {
        var name = $"mcp__{UnsafeNameCharacters.Replace(serverName, "_")}__{UnsafeNameCharacters.Replace(toolName, "_")}";
        if (name.Length <= MaxToolNameLength)
        {
            return name;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..8].ToLowerInvariant();
        return $"{name[..(MaxToolNameLength - 9)]}_{hash}";
    }

    /// <summary>
    /// Maps a server's tool hints to a risk: read-only tools are Read (Network when they reach outside),
    /// everything else is External, so Ask and Auto-edit modes both ask first.
    /// </summary>
    /// <param name="annotations">The tool's hints, if any.</param>
    public static ToolRisk ClassifyRisk(ToolAnnotations? annotations)
    {
        if (annotations?.ReadOnlyHint == true)
        {
            return annotations.OpenWorldHint == true ? ToolRisk.Network : ToolRisk.Read;
        }

        return ToolRisk.External;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        List<McpClient> clients;
        lock (_gate)
        {
            if (_shutdown.IsCancellationRequested)
            {
                return;
            }

            _shutdown.Cancel();
            clients = _servers.Values.Where(entry => entry.Client is not null).Select(entry => entry.Client!).ToList();
            _servers.Clear();
        }

        foreach (var client in clients)
        {
            await DisposeClientAsync(client);
        }
    }

    /// <inheritdoc />
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private void EnsureLoaded()
    {
        lock (_gate)
        {
            if (_loaded)
            {
                return;
            }

            foreach (var definition in ReadDefinitions().Values)
            {
                _servers[definition.Name] = new ServerEntry(definition);
            }

            _loaded = true;
        }
    }

    private Dictionary<string, McpServerDefinition> ReadDefinitions()
    {
        var definitions = new Dictionary<string, McpServerDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in _sources)
        {
            IReadOnlyList<McpServerDefinition> servers;
            try
            {
                servers = source.GetServers();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MCP server source {Source} failed", source.GetType().Name);
                continue;
            }

            foreach (var server in servers)
            {
                definitions.TryAdd(server.Name, server);
            }
        }

        return definitions;
    }

    private async Task ConnectAsync(ServerEntry entry)
    {
        await Task.Yield();
        McpClient? client = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            timeout.CancelAfter(_connectTimeout);
            var transport = _transportFactory(Resolve(entry.Definition));
            client = await McpClient.CreateAsync(
                transport,
                new McpClientOptions
                {
                    ClientInfo = new Implementation
                    {
                        Name = "Kam",
                        Version = typeof(McpHost).Assembly.GetName().Version?.ToString() ?? "1.0.0"
                    }
                },
                _loggerFactory,
                timeout.Token);

            var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
            var descriptors = tools
                .Select(tool => new AgentToolDescriptor(
                    new McpToolFunction(client, tool, ToToolName(entry.Definition.Name, tool.ProtocolTool.Name)),
                    ClassifyRisk(tool.ProtocolTool.Annotations),
                    $"mcp:{entry.Definition.Name}",
                    $"{tool.ProtocolTool.Title ?? tool.ProtocolTool.Annotations?.Title ?? tool.ProtocolTool.Name} ({entry.Definition.Name})"))
                .ToList();

            bool current;
            lock (_gate)
            {
                // A reload or shutdown while the server started drops its entry; don't leave the process running.
                current = !_shutdown.IsCancellationRequested
                    && _servers.TryGetValue(entry.Definition.Name, out var registered)
                    && ReferenceEquals(registered, entry);
                if (current)
                {
                    entry.Client = client;
                    entry.Tools = descriptors;
                    entry.Status = McpServerStatus.Ready;
                    entry.Error = null;
                    entry.Connecting = null;
                }
            }

            if (!current)
            {
                await DisposeClientAsync(client);
                return;
            }

            _logger.LogInformation("MCP server {Server} ready with {Count} tools", entry.Definition.Name, descriptors.Count);
        }
        catch (Exception ex)
        {
            if (client is not null)
            {
                await DisposeClientAsync(client);
            }

            if (_shutdown.IsCancellationRequested)
            {
                return;
            }

            var message = ex is OperationCanceledException
                ? $"Did not answer within {_connectTimeout.TotalSeconds:0} seconds."
                : ex.GetBaseException().Message;
            lock (_gate)
            {
                entry.Status = McpServerStatus.Failed;
                entry.Error = message;
                entry.Connecting = null;
            }

            _logger.LogWarning("MCP server {Server} failed to start: {Error}", entry.Definition.Name, message);
        }

        RaiseStateChanged();
    }

    private McpServerDefinition Resolve(McpServerDefinition definition)
    {
        // Only the user's own mcp.json may read saved secrets; plugin entries get environment variables.
        Func<string, string?>? secretLookup = string.Equals(definition.Source, "user", StringComparison.OrdinalIgnoreCase)
            ? name => _secrets?.GetSecret(name)
            : null;
        string Expand(string value) => McpVariableExpander.Expand(value, secretLookup);

        return definition with
        {
            Command = definition.Command is null ? null : Expand(definition.Command),
            Arguments = definition.Arguments.Select(Expand).ToArray(),
            Environment = definition.Environment.ToDictionary(pair => pair.Key, pair => Expand(pair.Value)),
            WorkingDirectory = definition.WorkingDirectory is null ? null : Expand(definition.WorkingDirectory),
            Url = definition.Url is null ? null : Expand(definition.Url),
            Headers = definition.Headers.ToDictionary(pair => pair.Key, pair => Expand(pair.Value))
        };
    }

    private IClientTransport CreateTransport(McpServerDefinition definition)
    {
        if (definition.Transport == McpTransportKind.Http)
        {
            return new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Name = definition.Name,
                    Endpoint = new Uri(definition.Url!),
                    AdditionalHeaders = definition.Headers.ToDictionary(pair => pair.Key, pair => pair.Value)
                },
                _loggerFactory);
        }

        return new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Name = definition.Name,
                Command = definition.Command!,
                Arguments = definition.Arguments.ToList(),
                WorkingDirectory = string.IsNullOrWhiteSpace(definition.WorkingDirectory) ? null : definition.WorkingDirectory,
                EnvironmentVariables = definition.Environment.ToDictionary(pair => pair.Key, pair => (string?)pair.Value)
            },
            _loggerFactory);
    }

    private async Task DisposeClientAsync(McpClient client)
    {
        try
        {
            await client.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Disposing an MCP client failed");
        }
    }

    private void RaiseStateChanged()
    {
        try
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP state listener failed");
        }
    }

    private static string Fingerprint(McpServerDefinition definition) => JsonSerializer.Serialize(definition);

    private sealed class ServerEntry(McpServerDefinition definition)
    {
        public McpServerDefinition Definition { get; } = definition;

        public McpServerStatus Status { get; set; } = definition.Disabled ? McpServerStatus.Disabled : McpServerStatus.Idle;

        public string? Error { get; set; }

        public McpClient? Client { get; set; }

        public IReadOnlyList<AgentToolDescriptor> Tools { get; set; } = [];

        public Task? Connecting { get; set; }
    }
}

/// <summary>
/// An MCP tool as an <see cref="AIFunction"/> with Kam's tool name; results come back as text.
/// </summary>
internal sealed class McpToolFunction : AIFunction
{
    private readonly McpClient _client;
    private readonly McpClientTool _tool;

    public McpToolFunction(McpClient client, McpClientTool tool, string name)
    {
        _client = client;
        _tool = tool;
        Name = name;
    }

    public override string Name { get; }

    public override string Description => _tool.Description;

    public override JsonElement JsonSchema => _tool.JsonSchema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var result = await _client.CallToolAsync(
            _tool.ProtocolTool.Name,
            new Dictionary<string, object?>(arguments),
            cancellationToken: cancellationToken);
        return McpResultFormatter.Format(result);
    }
}

/// <summary>
/// Turns an MCP tool result into the text the model sees.
/// </summary>
public static class McpResultFormatter
{
    /// <summary>
    /// Joins the result's text blocks; other blocks are described in brackets. Errors start with "Error:".
    /// </summary>
    /// <param name="result">The tool result.</param>
    public static string Format(CallToolResult result)
    {
        var parts = result.Content
            .Select(block => block switch
            {
                TextContentBlock text => text.Text,
                ImageContentBlock image => $"[image {image.MimeType}]",
                AudioContentBlock audio => $"[audio {audio.MimeType}]",
                EmbeddedResourceBlock { Resource: TextResourceContents resource } => resource.Text,
                EmbeddedResourceBlock embedded => $"[resource {embedded.Resource.Uri}]",
                ResourceLinkBlock link => $"[resource {link.Uri}]",
                _ => string.Empty
            })
            .Where(part => !string.IsNullOrEmpty(part));

        var text = string.Join("\n", parts);
        if (text.Length == 0 && result.StructuredContent is { } structured)
        {
            text = structured.ToString();
        }

        if (result.IsError == true)
        {
            return "Error: " + (text.Length == 0 ? "the tool reported a failure." : text);
        }

        return text.Length == 0 ? "Done." : text;
    }
}

/// <summary>
/// Offers the tools of every ready MCP server to the agent.
/// </summary>
public sealed class McpToolProvider : IAgentToolProvider
{
    private readonly IMcpHost _host;

    /// <summary>
    /// Creates the provider.
    /// </summary>
    /// <param name="host">The MCP host.</param>
    public McpToolProvider(IMcpHost host)
    {
        _host = host;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentToolDescriptor>> GetToolsAsync(CancellationToken cancellationToken = default) =>
        _host.GetToolsAsync(cancellationToken);
}
