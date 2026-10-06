using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Infrastructure.Agent.Conf;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Builds chat clients from model settings and reuses each one until the settings behind it change,
/// so a change in Settings applies to the next request without a restart.
/// </summary>
public sealed class ChatClientCache
{
    private const int MaxClients = 8;

    private readonly Func<AIServiceConfiguration, IChatClient> _create;
    private readonly object _gate = new();
    private readonly Dictionary<string, IChatClient> _clients = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();

    /// <summary>
    /// Creates the cache.
    /// </summary>
    /// <param name="create">Builds a client for one set of model settings.</param>
    public ChatClientCache(Func<AIServiceConfiguration, IChatClient> create)
    {
        _create = create;
    }

    /// <summary>
    /// Returns the client for these settings, building it the first time they are seen.
    /// </summary>
    /// <param name="configuration">Provider, endpoint, key and model.</param>
    public IChatClient Get(AIServiceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var key = KeyFor(configuration);
        lock (_gate)
        {
            if (_clients.TryGetValue(key, out var client))
            {
                return client;
            }

            client = _create(configuration);
            _clients[key] = client;
            _order.Enqueue(key);

            // Replaced clients are dropped, not disposed: a turn that started before the change may still use one.
            while (_order.Count > MaxClients)
            {
                _clients.Remove(_order.Dequeue());
            }

            return client;
        }
    }

    /// <summary>
    /// Returns a copy of the settings that runs another model on the same connection.
    /// </summary>
    /// <param name="configuration">The connection.</param>
    /// <param name="modelId">The model to run, or null to keep the configured one.</param>
    public static AIServiceConfiguration WithModel(AIServiceConfiguration configuration, string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)
            || string.Equals(modelId, configuration.ModelId, StringComparison.Ordinal))
        {
            return configuration;
        }

        return new AIServiceConfiguration
        {
            Provider = configuration.Provider,
            Endpoint = configuration.Endpoint,
            ApiKey = configuration.ApiKey,
            ModelId = modelId.Trim(),
            DefaultTemperature = configuration.DefaultTemperature,
            DefaultMaxTokens = configuration.DefaultMaxTokens
        };
    }

    private static string KeyFor(AIServiceConfiguration configuration)
    {
        // The key itself is not kept in the cache key, only a hash that changes with it.
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configuration.ApiKey ?? string.Empty)));
        return string.Join(
            '\n',
            configuration.Provider,
            configuration.Endpoint,
            configuration.ModelId,
            configuration.DefaultMaxTokens,
            configuration.DefaultTemperature,
            keyHash);
    }
}

/// <summary>
/// A chat client that looks up the configured client on every request, so services that keep one
/// for the life of the app still follow changes made in Settings.
/// </summary>
public sealed class ConfiguredChatClient : IChatClient
{
    private readonly Func<IChatClient> _resolve;

    /// <summary>
    /// Creates the client.
    /// </summary>
    /// <param name="resolve">Returns the client for the current settings; throws when none is set up.</param>
    public ConfiguredChatClient(Func<IChatClient> resolve)
    {
        _resolve = resolve;
    }

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return _resolve().GetResponseAsync(messages, options, cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in _resolve().GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            yield return update;
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this)
            ? this
            : _resolve().GetService(serviceType, serviceKey);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // The cache owns the clients this one hands out.
    }
}
