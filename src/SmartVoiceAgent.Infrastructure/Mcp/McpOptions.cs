namespace SmartVoiceAgent.Infrastructure.Mcp;

/// <summary>
/// Configuration options for MCP (Model Context Protocol) servers
/// </summary>
public class McpOptions
{
    #region Todoist MCP

    public string TodoistApiKey { get; set; } = string.Empty;
    public string TodoistServerLink { get; set; } = "todoist.mcpverse.dev";

    #endregion

    #region Slack MCP

    /// <summary>
    /// Slack Bot User OAuth Token (starts with xoxb-)
    /// </summary>
    public string SlackBotToken { get; set; } = string.Empty;

    /// <summary>
    /// Slack Team/Workspace ID (starts with T)
    /// </summary>
    public string SlackTeamId { get; set; } = string.Empty;

    /// <summary>
    /// Optional: Comma-separated list of allowed channel IDs (e.g., "C01234567,C76543210")
    /// If not set, all public channels will be accessible
    /// </summary>
    public string? SlackChannelIds { get; set; }

    /// <summary>
    /// Slack MCP Server endpoint. 
    /// Default uses the official ModelContextProtocol Slack server via NPX
    /// </summary>
    public string SlackServerLink { get; set; } = "slack-mcp-server";

    /// <summary>
    /// Transport type: 'stdio' (default) or 'http'
    /// </summary>
    public string SlackTransport { get; set; } = "stdio";

    /// <summary>
    /// HTTP port for Streamable HTTP transport (default: 3000)
    /// Only used when SlackTransport is 'http'
    /// </summary>
    public int SlackHttpPort { get; set; } = 3000;

    /// <summary>
    /// Optional: Bearer token for HTTP authorization
    /// Only used when SlackTransport is 'http'
    /// </summary>
    public string? SlackAuthToken { get; set; }

    /// <summary>
    /// Gets parsed list of allowed channel IDs
    /// </summary>
    public IEnumerable<string> GetAllowedChannelIds()
    {
        if (string.IsNullOrWhiteSpace(SlackChannelIds))
            return Array.Empty<string>();

        return SlackChannelIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => id.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id));
    }

    /// <summary>
    /// Validates that required Slack configuration is present
    /// </summary>
    public bool IsSlackConfigured()
    {
        return !string.IsNullOrWhiteSpace(SlackBotToken) &&
               SlackBotToken.StartsWith("xoxb-") &&
               !string.IsNullOrWhiteSpace(SlackTeamId) &&
               SlackTeamId.StartsWith("T");
    }

    #endregion
}
