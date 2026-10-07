using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents.Extensions;
using SmartVoiceAgent.Infrastructure.Mcp;

namespace SmartVoiceAgent.Infrastructure.Agent.Mcp;

/// <summary>
/// MCP servers from the user's <c>%AppData%/Kam/mcp.json</c>.
/// </summary>
public sealed class UserMcpServerSource : IMcpServerSource
{
    private static readonly JsonDocumentOptions EditOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ILogger<UserMcpServerSource>? _logger;

    /// <summary>
    /// Creates the source.
    /// </summary>
    /// <param name="configPath">The configuration file.</param>
    /// <param name="logger">Logger for configuration errors.</param>
    public UserMcpServerSource(string configPath, ILogger<UserMcpServerSource>? logger = null)
    {
        ConfigPath = configPath;
        _logger = logger;
    }

    /// <summary>Gets the configuration file.</summary>
    public string ConfigPath { get; }

    /// <summary>Gets the problems found the last time the file was read.</summary>
    public IReadOnlyList<string> LastErrors { get; private set; } = [];

    /// <summary>The default location, <c>%AppData%/Kam/mcp.json</c>.</summary>
    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Kam",
        "mcp.json");

    /// <inheritdoc />
    public IReadOnlyList<McpServerDefinition> GetServers()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                LastErrors = [];
                return [];
            }

            var (servers, errors) = McpConfigParser.Parse(File.ReadAllText(ConfigPath), "user");
            LastErrors = errors;
            foreach (var error in errors)
            {
                _logger?.LogWarning("mcp.json: {Error}", error);
            }

            return servers;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastErrors = [ex.Message];
            _logger?.LogWarning(ex, "Could not read {Path}", ConfigPath);
            return [];
        }
    }

    /// <summary>
    /// Writes an empty configuration with an example entry, when the file does not exist yet.
    /// </summary>
    public void EnsureExists()
    {
        if (File.Exists(ConfigPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(
            ConfigPath,
            """
            {
              "mcpServers": {
                "example-filesystem": {
                  "command": "npx",
                  "args": ["-y", "@modelcontextprotocol/server-filesystem", "${USERPROFILE}/Documents"],
                  "disabled": true
                }
              }
            }
            """);
    }

    /// <summary>
    /// Adds a server under <c>mcpServers</c>, or replaces the one with the same name. Other entries and settings
    /// in the file are kept; comments are not.
    /// </summary>
    /// <param name="name">The server name.</param>
    /// <param name="entry">The entry, in the <c>mcpServers</c> format.</param>
    /// <exception cref="InvalidOperationException">The file is not a JSON object.</exception>
    public void AddServer(string name, JsonObject entry)
    {
        Edit(servers =>
        {
            servers[name] = entry.DeepClone();
            return true;
        });
    }

    /// <summary>
    /// Removes a server from the file.
    /// </summary>
    /// <param name="name">The server name.</param>
    /// <returns>Whether the server was there.</returns>
    /// <exception cref="InvalidOperationException">The file is not a JSON object.</exception>
    public bool RemoveServer(string name) => File.Exists(ConfigPath) && Edit(servers => servers.Remove(name));

    /// <summary>
    /// Turns a server on or off with its <c>disabled</c> flag.
    /// </summary>
    /// <param name="name">The server name.</param>
    /// <param name="enabled">Whether the server should run.</param>
    /// <returns>Whether the server was there.</returns>
    /// <exception cref="InvalidOperationException">The file is not a JSON object.</exception>
    public bool SetServerEnabled(string name, bool enabled) => File.Exists(ConfigPath) && Edit(servers =>
    {
        if (servers[name] is not JsonObject entry)
        {
            return false;
        }

        entry.Remove("enabled");
        if (enabled)
        {
            entry.Remove("disabled");
        }
        else
        {
            entry["disabled"] = true;
        }

        return true;
    });

    private bool Edit(Func<JsonObject, bool> change)
    {
        JsonObject root;
        if (File.Exists(ConfigPath) && File.ReadAllText(ConfigPath) is { } text && !string.IsNullOrWhiteSpace(text))
        {
            try
            {
                root = JsonNode.Parse(text, documentOptions: EditOptions) as JsonObject
                    ?? throw new InvalidOperationException("mcp.json must contain a JSON object.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"mcp.json is not valid JSON: {ex.Message}", ex);
            }
        }
        else
        {
            root = new JsonObject();
        }

        // Servers live under mcpServers, or at the root in files that list them there.
        var servers = root["mcpServers"] as JsonObject;
        if (servers is null)
        {
            var listsServersAtRoot = root.Count > 0 && root.All(property =>
                property.Value is JsonObject server && (server.ContainsKey("command") || server.ContainsKey("url")));
            if (listsServersAtRoot)
            {
                servers = root;
            }
            else
            {
                servers = new JsonObject();
                root["mcpServers"] = servers;
            }
        }

        if (!change(servers))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ConfigPath))!);
        File.WriteAllText(ConfigPath, root.ToJsonString(WriteOptions) + Environment.NewLine);
        return true;
    }
}

/// <summary>
/// The Todoist integration from Settings, offered as an ordinary MCP server named <c>todoist</c>.
/// </summary>
public sealed class TodoistMcpServerSource : IMcpServerSource
{
    private readonly IOptionsMonitor<McpOptions> _options;

    /// <summary>
    /// Creates the source.
    /// </summary>
    /// <param name="options">Todoist settings.</param>
    public TodoistMcpServerSource(IOptionsMonitor<McpOptions> options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public IReadOnlyList<McpServerDefinition> GetServers()
    {
        var options = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.TodoistApiKey)
            || !Uri.TryCreate(options.TodoistServerLink, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https"))
        {
            return [];
        }

        return
        [
            new McpServerDefinition
            {
                Name = "todoist",
                Transport = McpTransportKind.Http,
                Url = endpoint.ToString(),
                Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {options.TodoistApiKey}" },
                Source = "built-in"
            }
        ];
    }
}
