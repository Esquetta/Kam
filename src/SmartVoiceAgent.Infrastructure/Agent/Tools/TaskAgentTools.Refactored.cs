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
    /// Refactored TaskAgentTools with proper error handling, resilience, and observability.
    /// </summary>
    public sealed class TaskAgentToolsRefactored
    {
        private readonly McpOptions _mcpOptions;
        private readonly ILogger<TaskAgentToolsRefactored>? _logger;
        private IEnumerable<AIFunction>? _mcpTools;
        private bool _isInitialized;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        // Retry configuration
        private const int MaxRetries = 3;
        private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan InitializationTimeout = TimeSpan.FromSeconds(30);

        public TaskAgentToolsRefactored(
            IOptions<McpOptions> options,
            ILogger<TaskAgentToolsRefactored>? logger = null)
        {
            _mcpOptions = options.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger;
        }

        /// <summary>
        /// Initializes the MCP client with retry logic and timeout.
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_isInitialized)
            {
                _logger?.LogDebug("TaskAgentTools already initialized");
                return;
            }

            await _initLock.WaitAsync(cancellationToken);
            try
            {
                if (_isInitialized) // Double-check after acquiring lock
                    return;

                _logger?.LogInformation("Initializing TaskAgentTools with MCP server: {Server}",
                    _mcpOptions.TodoistServerLink);

                var stopwatch = Stopwatch.StartNew();

                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(InitializationTimeout);

                    var client = await InitializeWithRetryAsync(cts.Token);
                    _mcpTools = await ListToolsWithRetryAsync(client, cts.Token).ConfigureAwait(false);

                    _isInitialized = true;
                    stopwatch.Stop();

                    _logger?.LogInformation(
                        "TaskAgentTools initialized successfully in {ElapsedMs}ms. Loaded {ToolCount} tools",
                        stopwatch.ElapsedMilliseconds,
                        _mcpTools?.Count() ?? 0);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger?.LogError("MCP initialization timed out after {Timeout}s",
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
        /// Initializes MCP client with exponential backoff retry.
        /// </summary>
        private async Task<McpClient> InitializeWithRetryAsync(CancellationToken cancellationToken)
        {
            var retryCount = 0;
            var delay = InitialRetryDelay;

            while (true)
            {
                try
                {
                    _logger?.LogDebug("Attempting MCP client initialization (attempt {Attempt})", retryCount + 1);

                    var client = await McpClient.CreateAsync(
                        clientTransport: new HttpClientTransport(new()
                        {
                            Endpoint = new Uri(_mcpOptions.TodoistServerLink),
                            Name = "todoist.mcpverse.dev",
                            AdditionalHeaders = new Dictionary<string, string>
                            {
                                ["Authorization"] = $"Bearer {_mcpOptions.TodoistApiKey}"
                            }
                        }),
                        clientOptions: new McpClientOptions
                        {
                            ClientInfo = new Implementation()
                            {
                                Name = "SmartVoiceAgent.MCP.Client",
                                Version = typeof(TaskAgentToolsRefactored).Assembly.GetName().Version?.ToString() ?? "1.0.0"
                            }
                        },
                        cancellationToken: cancellationToken);

                    return client;
                }
                catch (Exception ex) when (retryCount < MaxRetries && IsRetryableException(ex))
                {
                    retryCount++;
                    _logger?.LogWarning(ex,
                        "MCP initialization failed (attempt {Attempt}/{MaxRetries}), retrying in {DelayMs}ms...",
                        retryCount, MaxRetries, delay.TotalMilliseconds);

                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, MaxRetryDelay.TotalMilliseconds));
                }
            }
        }

        /// <summary>
        /// Lists available tools from MCP server with retry logic.
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
                    _logger?.LogDebug("Listing MCP tools (attempt {Attempt})", retryCount + 1);
                    // Note: ListToolsAsync doesn't directly support CancellationToken in current MCP version
                    var tools = await client.ListToolsAsync();
                    return tools?.Cast<AIFunction>() ?? Array.Empty<AIFunction>();
                }
                catch (Exception ex) when (retryCount < MaxRetries && IsRetryableException(ex))
                {
                    retryCount++;
                    _logger?.LogWarning(ex,
                        "Failed to list MCP tools (attempt {Attempt}/{MaxRetries}), retrying in {DelayMs}ms...",
                        retryCount, MaxRetries, delay.TotalMilliseconds);

                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, MaxRetryDelay.TotalMilliseconds));
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
                _logger?.LogWarning("GetTools() called before initialization. Call InitializeAsync() first.");
                return Array.Empty<AIFunction>();
            }

            return _mcpTools ?? Array.Empty<AIFunction>();
        }

        /// <summary>
        /// Health check for MCP connection.
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
                _logger?.LogError(ex, "Health check failed");
                return false;
            }
        }
    }
}
