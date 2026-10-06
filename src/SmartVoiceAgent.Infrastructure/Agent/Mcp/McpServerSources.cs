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
