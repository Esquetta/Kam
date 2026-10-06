using System.Text.Json;
using SmartVoiceAgent.Core.Models.Agents.Extensions;

namespace SmartVoiceAgent.Infrastructure.Agent.Mcp;

/// <summary>
/// Reads MCP server entries in the <c>mcpServers</c> format shared by Claude Desktop, Claude Code and Cursor,
/// so existing configurations can be pasted in as they are.
/// </summary>
public static class McpConfigParser
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Parses a configuration document. The servers may sit under <c>mcpServers</c> or at the root.
    /// </summary>
    /// <param name="json">The document text.</param>
    /// <param name="source">Where the entries come from, such as <c>user</c> or <c>plugin:github</c>.</param>
    /// <param name="variables">Values replaced right away, such as <c>CLAUDE_PLUGIN_ROOT</c>. Other references are
    /// kept and resolved when the server starts.</param>
    /// <returns>The valid entries, and a message for each entry that was skipped.</returns>
    public static (IReadOnlyList<McpServerDefinition> Servers, IReadOnlyList<string> Errors) Parse(
        string json,
        string source,
        IReadOnlyDictionary<string, string>? variables = null)
    {
        var servers = new List<McpServerDefinition>();
        var errors = new List<string>();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException ex)
        {
            errors.Add($"Not valid JSON: {ex.Message}");
            return (servers, errors);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add("The file must contain a JSON object.");
                return (servers, errors);
            }

            var map = document.RootElement.TryGetProperty("mcpServers", out var nested) && nested.ValueKind == JsonValueKind.Object
                ? nested
                : document.RootElement;

            foreach (var property in map.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var (server, error) = ParseServer(property.Name, property.Value, source, variables);
                if (server is not null)
                {
                    servers.Add(server);
                }
                else if (error is not null)
                {
                    errors.Add(error);
                }
            }
        }

        return (servers, errors);
    }

    /// <summary>
    /// Parses a single server entry, as found inside <c>mcpServers</c>.
    /// </summary>
    /// <param name="name">The server name.</param>
    /// <param name="entry">The entry object.</param>
    /// <param name="source">Where the entry comes from.</param>
    /// <param name="variables">Values replaced right away.</param>
    public static (McpServerDefinition? Server, string? Error) ParseServer(
        string name,
        JsonElement entry,
        string source,
        IReadOnlyDictionary<string, string>? variables = null)
    {
        string? Read(string key) =>
            entry.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                ? Substitute(value.GetString(), variables)
                : null;

        var type = Read("type")?.ToLowerInvariant();
        var command = Read("command");
        var url = Read("url");
        var isHttp = type is "http" or "sse" or "streamable-http" or "streamablehttp"
            || (type is null && command is null && url is not null);

        var disabled = (entry.TryGetProperty("disabled", out var disabledValue) && disabledValue.ValueKind == JsonValueKind.True)
            || (entry.TryGetProperty("enabled", out var enabledValue) && enabledValue.ValueKind == JsonValueKind.False);

        if (isHttp)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return (null, $"Server '{name}' needs a \"url\".");
            }

            return (new McpServerDefinition
            {
                Name = name,
                Transport = McpTransportKind.Http,
                Url = url,
                Headers = ReadMap(entry, "headers", variables),
                Source = source,
                Disabled = disabled
            }, null);
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            return (null, $"Server '{name}' needs a \"command\" or a \"url\".");
        }

        var arguments = entry.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array
            ? args.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => Substitute(item.GetString(), variables) ?? string.Empty)
                .ToArray()
            : [];

        return (new McpServerDefinition
        {
            Name = name,
            Transport = McpTransportKind.Stdio,
            Command = command,
            Arguments = arguments,
            Environment = ReadMap(entry, "env", variables),
            WorkingDirectory = Read("cwd"),
            Source = source,
            Disabled = disabled
        }, null);
    }

    private static Dictionary<string, string> ReadMap(
        JsonElement entry,
        string key,
        IReadOnlyDictionary<string, string>? variables)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (entry.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in value.EnumerateObject())
            {
                if (item.Value.ValueKind == JsonValueKind.String)
                {
                    map[item.Name] = Substitute(item.Value.GetString(), variables) ?? string.Empty;
                }
            }
        }

        return map;
    }

    private static string? Substitute(string? value, IReadOnlyDictionary<string, string>? variables)
    {
        if (value is null || variables is null || variables.Count == 0)
        {
            return value;
        }

        foreach (var (key, replacement) in variables)
        {
            value = value.Replace("${" + key + "}", replacement, StringComparison.Ordinal);
        }

        return value;
    }
}
