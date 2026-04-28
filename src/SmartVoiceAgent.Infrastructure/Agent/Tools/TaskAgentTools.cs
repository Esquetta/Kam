using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SmartVoiceAgent.Infrastructure.Mcp;
using System.Diagnostics;
using System.Net.Sockets;

namespace SmartVoiceAgent.Infrastructure.Agent.Tools
{
    /// <summary>
    /// TaskAgentTools for task management via MCP (Model Context Protocol).
    /// Includes error handling, retry logic, and timeout protection.
    /// </summary>
    public sealed class TaskAgentTools
    {
        private readonly McpOptions _mcpOptions;
        private readonly ILogger<TaskAgentTools>? _logger;
        private IEnumerable<AIFunction>? _mcpTools;
        private bool _isInitialized;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        // Retry configuration
        private const int MaxRetries = 3;
        private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan InitializationTimeout = TimeSpan.FromSeconds(30);

        public TaskAgentTools(
            IOptions<McpOptions> options,
            ILogger<TaskAgentTools>? logger = null)
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

                if (!TryCreateTodoistEndpoint(out var endpoint))
                {
                    _logger?.LogInformation(
                        "Todoist MCP is not configured. Task MCP tools are disabled.");
                    _mcpTools = Array.Empty<AIFunction>();
                    _isInitialized = true;
                    return;
                }

                _logger?.LogInformation("Initializing TaskAgentTools with MCP server: {Server}",
                    endpoint);

                var stopwatch = Stopwatch.StartNew();

                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(InitializationTimeout);

                    var client = await InitializeWithRetryAsync(endpoint, cts.Token);
                    _mcpTools = await ListToolsWithRetryAsync(client, cts.Token);

                    _isInitialized = true;
                    stopwatch.Stop();

                    _logger?.LogInformation(
                        "TaskAgentTools initialized successfully in {ElapsedMs}ms. Loaded {ToolCount} tools",
                        stopwatch.ElapsedMilliseconds,
                        _mcpTools?.Count() ?? 0);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger?.LogWarning("MCP initialization timed out after {Timeout}s. Task MCP tools are disabled.",
                        InitializationTimeout.TotalSeconds);
                    _mcpTools = Array.Empty<AIFunction>();
                    _isInitialized = true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (IsEndpointUnavailableException(ex))
                {
                    _logger?.LogWarning(
                        "MCP endpoint {Server} is unavailable ({Reason}). Task MCP tools are disabled.",
                        endpoint,
                        GetEndpointUnavailableReason(ex));
                    _mcpTools = Array.Empty<AIFunction>();
                    _isInitialized = true;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "MCP client unavailable. Task MCP tools are disabled.");
                    _mcpTools = Array.Empty<AIFunction>();
                    _isInitialized = true;
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
        private async Task<McpClient> InitializeWithRetryAsync(Uri endpoint, CancellationToken cancellationToken)
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
                            Endpoint = endpoint,
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
                                Version = typeof(TaskAgentTools).Assembly.GetName().Version?.ToString() ?? "1.0.0"
                            }
                        });

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
            if (IsEndpointUnavailableException(ex))
            {
                return false;
            }

            return ex is HttpRequestException
                or TimeoutException
                or IOException
                or SocketException;
        }

        private static bool IsEndpointUnavailableException(Exception ex)
        {
            return FindSocketException(ex) is
            {
                SocketErrorCode: SocketError.HostNotFound or SocketError.NoData
            };
        }

        private static string GetEndpointUnavailableReason(Exception ex)
        {
            return FindSocketException(ex)?.SocketErrorCode.ToString() ?? ex.GetType().Name;
        }

        private static SocketException? FindSocketException(Exception ex)
        {
            var current = ex;
            while (current is not null)
            {
                if (current is SocketException socketException)
                {
                    return socketException;
                }

                current = current.InnerException;
            }

            return null;
        }

        private bool TryCreateTodoistEndpoint(out Uri endpoint)
        {
            endpoint = null!;

            if (string.IsNullOrWhiteSpace(_mcpOptions.TodoistApiKey)
                || string.IsNullOrWhiteSpace(_mcpOptions.TodoistServerLink))
            {
                return false;
            }

            var serverLink = _mcpOptions.TodoistServerLink.Trim();
            if (Uri.TryCreate(serverLink, UriKind.Absolute, out var absoluteEndpoint))
            {
                if (IsHttpEndpoint(absoluteEndpoint))
                {
                    endpoint = absoluteEndpoint;
                    return true;
                }

                return false;
            }

            return TryCreateHttpEndpoint($"https://{serverLink}", out endpoint);
        }

        private static bool TryCreateHttpEndpoint(string value, out Uri endpoint)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out endpoint!)
                && IsHttpEndpoint(endpoint))
            {
                return true;
            }

            endpoint = null!;
            return false;
        }

        private static bool IsHttpEndpoint(Uri endpoint)
        {
            return endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps;
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
