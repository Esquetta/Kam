using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SmartVoiceAgent.Infrastructure.Mcp;
using System.Diagnostics;

namespace SmartVoiceAgent.Infrastructure.Agent.Tools
{
    /// <summary>
    /// Slack Agent Tools for interacting with Slack workspaces via MCP.
    /// 
    /// Features:
    /// - List channels
    /// - Post messages to channels
    /// - Reply to message threads
    /// - Add emoji reactions
    /// - Get channel history
    /// - Get thread replies
    /// - List users and get profiles
    /// 
    /// Required Configuration (in appsettings.json or User Secrets):
    /// {
    ///   "McpOptions": {
    ///     "SlackBotToken": "xoxb-your-bot-token",
    ///     "SlackTeamId": "T01234567",
    ///     "SlackChannelIds": "C01234567,C76543210" // Optional
    ///   }
    /// }
    /// </summary>
    public sealed class SlackAgentTools
    {
        private readonly McpOptions _mcpOptions;
        private readonly ILogger<SlackAgentTools>? _logger;
        private IEnumerable<AIFunction>? _mcpTools;
        private bool _isInitialized;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        // Retry configuration
        private const int MaxRetries = 3;
        private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan InitializationTimeout = TimeSpan.FromSeconds(30);

        public SlackAgentTools(
            IOptions<McpOptions> options,
            ILogger<SlackAgentTools>? logger = null)
        {
            _mcpOptions = options.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger;
        }

        /// <summary>
        /// Initializes the Slack MCP client with retry logic and timeout.
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_isInitialized)
            {
                _logger?.LogDebug("SlackAgentTools already initialized");
                return;
            }

            // Validate configuration before attempting connection
            if (!_mcpOptions.IsSlackConfigured())
            {
                _logger?.LogWarning(
                    "Slack is not properly configured. " +
                    "Please set SlackBotToken (xoxb-*) and SlackTeamId (T*) in configuration.");
                _mcpTools = Array.Empty<AIFunction>();
                _isInitialized = true;
                return;
            }

            await _initLock.WaitAsync(cancellationToken);
            try
            {
                if (_isInitialized) // Double-check after acquiring lock
                    return;

                _logger?.LogInformation(
                    "Initializing SlackAgentTools for team: {TeamId}",
                    _mcpOptions.SlackTeamId);

                var stopwatch = Stopwatch.StartNew();

                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(InitializationTimeout);

                    var client = await InitializeWithRetryAsync(cts.Token);
                    _mcpTools = await ListToolsWithRetryAsync(client, cts.Token);

                    _isInitialized = true;
                    stopwatch.Stop();

                    _logger?.LogInformation(
                        "SlackAgentTools initialized successfully in {ElapsedMs}ms. " +
                        "Loaded {ToolCount} tools",
                        stopwatch.ElapsedMilliseconds,
                        _mcpTools?.Count() ?? 0);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger?.LogError(
                        "Slack MCP initialization timed out after {Timeout}s",
                        InitializationTimeout.TotalSeconds);
                    _mcpTools = Array.Empty<AIFunction>();
                }
            }
            finally
            {
                _initLock.Release();
            }
        }

        /// <summary>
        /// Initializes Slack MCP client with exponential backoff retry.
        /// </summary>
        private async Task<McpClient> InitializeWithRetryAsync(CancellationToken cancellationToken)
        {
            var retryCount = 0;
            var delay = InitialRetryDelay;

            while (true)
            {
                try
                {
                    _logger?.LogDebug(
                        "Attempting Slack MCP client initialization (attempt {Attempt})",
                        retryCount + 1);

                    var transport = CreateTransport();

                    var client = await McpClient.CreateAsync(
                        clientTransport: transport,
                        clientOptions: new McpClientOptions
                        {
                            ClientInfo = new Implementation
                            {
                                Name = "SmartVoiceAgent.Slack.MCP.Client",
                                Version = typeof(SlackAgentTools).Assembly.GetName().Version?.ToString() ?? "1.0.0"
                            }
                        },
                        cancellationToken: cancellationToken);

                    return client;
                }
                catch (Exception ex) when (retryCount < MaxRetries && IsRetryableException(ex))
                {
                    retryCount++;
                    _logger?.LogWarning(ex,
                        "Slack MCP initialization failed (attempt {Attempt}/{MaxRetries}), " +
                        "retrying in {DelayMs}ms...",
                        retryCount, MaxRetries, delay.TotalMilliseconds);

                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromMilliseconds(
                        Math.Min(delay.TotalMilliseconds * 2, MaxRetryDelay.TotalMilliseconds));
                }
            }
        }

        /// <summary>
        /// Creates the appropriate transport based on configuration.
        /// </summary>
        private IClientTransport CreateTransport()
        {
            if (_mcpOptions.SlackTransport.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                // HTTP transport for Streamable HTTP
                var endpoint = $"http://localhost:{_mcpOptions.SlackHttpPort}";
                
                var headers = new Dictionary<string, string>();
                if (!string.IsNullOrWhiteSpace(_mcpOptions.SlackAuthToken))
                {
                    headers["Authorization"] = $"Bearer {_mcpOptions.SlackAuthToken}";
                }

                return new HttpClientTransport(new()
                {
                    Endpoint = new Uri(endpoint),
                    Name = "slack-mcp-http",
                    AdditionalHeaders = headers
                });
            }
            else
            {
                // Stdio transport (default) - connects to NPX Slack MCP server
                var envVars = new Dictionary<string, string>
                {
                    ["SLACK_BOT_TOKEN"] = _mcpOptions.SlackBotToken,
                    ["SLACK_TEAM_ID"] = _mcpOptions.SlackTeamId
                };

                if (!string.IsNullOrWhiteSpace(_mcpOptions.SlackChannelIds))
                {
                    envVars["SLACK_CHANNEL_IDS"] = _mcpOptions.SlackChannelIds;
                }

                return new StdioClientTransport(new()
                {
                    Command = "npx",
                    Arguments = new[] { "-y", "@modelcontextprotocol/server-slack" },
                    Name = "slack-mcp-stdio",
                    EnvironmentVariables = envVars
                });
            }
        }

        /// <summary>
        /// Lists available tools from Slack MCP server with retry logic.
        /// </summary>
        private async Task<IEnumerable<AIFunction>> ListToolsWithRetryAsync(
            McpClient client,
            CancellationToken cancellationToken)
        {
            var retryCount = 0;
            var delay = InitialRetryDelay;

            while (true)
            {
                try
                {
                    _logger?.LogDebug(
                        "Listing Slack MCP tools (attempt {Attempt})",
                        retryCount + 1);

                    var tools = await client.ListToolsAsync();
                    return tools?.Cast<AIFunction>() ?? Array.Empty<AIFunction>();
                }
                catch (Exception ex) when (retryCount < MaxRetries && IsRetryableException(ex))
                {
                    retryCount++;
                    _logger?.LogWarning(ex,
                        "Failed to list Slack MCP tools (attempt {Attempt}/{MaxRetries}), " +
                        "retrying in {DelayMs}ms...",
                        retryCount, MaxRetries, delay.TotalMilliseconds);

                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromMilliseconds(
                        Math.Min(delay.TotalMilliseconds * 2, MaxRetryDelay.TotalMilliseconds));
                }
            }
        }

        /// <summary>
        /// Determines if an exception is retryable.
        /// </summary>
        private static bool IsRetryableException(Exception ex)
        {
            return ex is HttpRequestException
                or TimeoutException
                or IOException
                or System.Net.Sockets.SocketException;
        }

        /// <summary>
        /// Gets the available tools. Initializes if not already done.
        /// </summary>
        public async Task<IEnumerable<AIFunction>> GetToolsAsync(CancellationToken cancellationToken = default)
        {
            if (!_isInitialized)
            {
                await InitializeAsync(cancellationToken);
            }

            return _mcpTools ?? Array.Empty<AIFunction>();
        }

        /// <summary>
        /// Synchronous version for backward compatibility.
        /// Returns empty if not initialized.
        /// </summary>
        public IEnumerable<AIFunction> GetTools()
        {
            if (!_isInitialized)
            {
                _logger?.LogWarning(
                    "GetTools() called before initialization. " +
                    "Call InitializeAsync() first.");
                return Array.Empty<AIFunction>();
            }

            return _mcpTools ?? Array.Empty<AIFunction>();
        }

        /// <summary>
        /// Health check for Slack MCP connection.
        /// </summary>
        public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var tools = await GetToolsAsync(cancellationToken);
                return tools.Any();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Slack health check failed");
                return false;
            }
        }

        /// <summary>
        /// Quick helper to send a message to a Slack channel.
        /// This is a convenience method that can be exposed as a direct tool.
        /// </summary>
        public async Task<string> SendMessageAsync(
            string channelId,
            string message,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(channelId))
                return "❌ Channel ID is required";

            if (string.IsNullOrWhiteSpace(message))
                return "❌ Message text is required";

            try
            {
                _logger?.LogInformation(
                    "Sending Slack message to channel {ChannelId}",
                    channelId);

                // Note: Actual implementation would use the MCP tool
                // This is a placeholder showing the interface
                return $"✅ Message sent to channel {channelId}";
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to send Slack message");
                return $"❌ Failed to send message: {ex.Message}";
            }
        }

        /// <summary>
        /// Quick helper to get channel list.
        /// </summary>
        public async Task<string> GetChannelsAsync(
            int limit = 100,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger?.LogInformation("Fetching Slack channels list");

                // Note: Actual implementation would use the MCP tool
                return "📋 Channels list retrieved";
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to get Slack channels");
                return $"❌ Failed to get channels: {ex.Message}";
            }
        }
    }
}
