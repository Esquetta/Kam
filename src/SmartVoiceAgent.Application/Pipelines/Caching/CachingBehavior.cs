using Core.CrossCuttingConcerns.Logging.Serilog;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;

namespace SmartVoiceAgent.Application.Pipelines.Caching;

public class CachingBehavior<TRequest, TResponse> :
    ICommandPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>, ICachableRequest
{
    private readonly IDistributedCache _cache;
    private readonly CacheSettings _cacheSettings;
    private readonly LoggerServiceBase _logger;
    private static readonly UTF8Encoding Utf8Encoding = new(false);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CachingBehavior(IDistributedCache cache, LoggerServiceBase logger, IConfiguration configuration)
    {
        _cache = cache;
        _logger = logger;
        _cacheSettings = configuration.GetSection("CacheSettings").Get<CacheSettings>()
            ?? new CacheSettings { SlidingExpiration = 1 };
    }

    public Task<TResponse> Handle(TRequest request, CommandHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        return HandleCore(request, next.Invoke, cancellationToken);
    }

    private async Task<TResponse> HandleCore(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
    {
        if (request.BypassCache)
        {
            return await next();
        }

        byte[]? cachedResponse = await _cache.GetAsync(request.CacheKey, cancellationToken);
        if (cachedResponse != null)
        {
            TResponse? response = JsonSerializer.Deserialize<TResponse>(cachedResponse, s_jsonOptions);
            if (response != null)
            {
                _logger.Info($"Cache hit: {request.CacheKey}");
                return response;
            }
        }

        return await GetResponseAndAddToCache(request, next, cancellationToken);
    }

    private async Task<TResponse> GetResponseAndAddToCache(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        TResponse response = await next();

        TimeSpan slidingExpiration = request.SlidingExpiration
            ?? TimeSpan.FromDays(_cacheSettings.SlidingExpiration);

        DistributedCacheEntryOptions cacheOptions = new() { SlidingExpiration = slidingExpiration };

        byte[] serializeData = JsonSerializer.SerializeToUtf8Bytes(response, s_jsonOptions);
        await _cache.SetAsync(request.CacheKey, serializeData, cacheOptions, cancellationToken);
        _logger.Info($"Cache add: {request.CacheKey}");

        if (request.CacheGroupKey != null)
        {
            await AddCacheKeyToGroup(request, slidingExpiration, cancellationToken);
        }

        return response;
    }

    private async Task AddCacheKeyToGroup(TRequest request, TimeSpan slidingExpiration, CancellationToken cancellationToken)
    {
        var cacheGroupTask = _cache.GetAsync(key: request.CacheGroupKey!, cancellationToken);
        var expirationTask = _cache.GetAsync(
            key: $"{request.CacheGroupKey}SlidingExpiration",
            cancellationToken);

        await Task.WhenAll(cacheGroupTask, expirationTask);

        byte[]? cacheGroupCache = cacheGroupTask.Result;
        byte[]? existingExpirationCache = expirationTask.Result;

        HashSet<string> cacheKeysInGroup;

        if (cacheGroupCache != null)
        {
            cacheKeysInGroup = JsonSerializer.Deserialize<HashSet<string>>(cacheGroupCache, s_jsonOptions)
                ?? new HashSet<string>(StringComparer.Ordinal);
            cacheKeysInGroup.Add(request.CacheKey);
        }
        else
        {
            cacheKeysInGroup = new HashSet<string>(StringComparer.Ordinal) { request.CacheKey };
        }

        byte[] newCacheGroupCache = JsonSerializer.SerializeToUtf8Bytes(cacheKeysInGroup, s_jsonOptions);

        int cacheGroupSlidingExpirationValue = (int)slidingExpiration.TotalSeconds;

        if (existingExpirationCache != null
            && int.TryParse(Utf8Encoding.GetString(existingExpirationCache), out int existingValue))
        {
            cacheGroupSlidingExpirationValue = Math.Max(cacheGroupSlidingExpirationValue, existingValue);
        }

        byte[] serializedExpirationData = Utf8Encoding.GetBytes(cacheGroupSlidingExpirationValue.ToString());

        DistributedCacheEntryOptions cacheOptions = new()
        {
            SlidingExpiration = TimeSpan.FromSeconds(cacheGroupSlidingExpirationValue)
        };

        var setGroupTask = _cache.SetAsync(key: request.CacheGroupKey!, newCacheGroupCache, cacheOptions, cancellationToken);
        var setExpirationTask = _cache.SetAsync(
            key: $"{request.CacheGroupKey}SlidingExpiration",
            serializedExpirationData,
            cacheOptions,
            cancellationToken);

        await Task.WhenAll(setGroupTask, setExpirationTask);

        _logger.Info($"Cache group: {request.CacheGroupKey}");
    }
}

public class QueryCachingBehavior<TRequest, TResponse> :
    IQueryPipelineBehavior<TRequest, TResponse>
    where TRequest : IQuery<TResponse>, ICachableRequest
{
    private readonly CachingBehaviorCore<TRequest, TResponse> _core;

    public QueryCachingBehavior(IDistributedCache cache, LoggerServiceBase logger, IConfiguration configuration)
    {
        _core = new CachingBehaviorCore<TRequest, TResponse>(cache, logger, configuration);
    }

    public Task<TResponse> Handle(TRequest request, QueryHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        return _core.HandleCore(request, next.Invoke, cancellationToken);
    }
}

internal sealed class CachingBehaviorCore<TRequest, TResponse>
    where TRequest : ICachableRequest
{
    private readonly IDistributedCache _cache;
    private readonly CacheSettings _cacheSettings;
    private readonly LoggerServiceBase _logger;
    private static readonly UTF8Encoding Utf8Encoding = new(false);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CachingBehaviorCore(IDistributedCache cache, LoggerServiceBase logger, IConfiguration configuration)
    {
        _cache = cache;
        _logger = logger;
        _cacheSettings = configuration.GetSection("CacheSettings").Get<CacheSettings>()
            ?? new CacheSettings { SlidingExpiration = 1 };
    }

    public async Task<TResponse> HandleCore(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
    {
        if (request.BypassCache)
        {
            return await next();
        }

        byte[]? cachedResponse = await _cache.GetAsync(request.CacheKey, cancellationToken);
        if (cachedResponse != null)
        {
            TResponse? response = JsonSerializer.Deserialize<TResponse>(cachedResponse, s_jsonOptions);
            if (response != null)
            {
                _logger.Info($"Cache hit: {request.CacheKey}");
                return response;
            }
        }

        return await GetResponseAndAddToCache(request, next, cancellationToken);
    }

    private async Task<TResponse> GetResponseAndAddToCache(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
    {
        TResponse response = await next();

        TimeSpan slidingExpiration = request.SlidingExpiration
            ?? TimeSpan.FromDays(_cacheSettings.SlidingExpiration);

        DistributedCacheEntryOptions cacheOptions = new() { SlidingExpiration = slidingExpiration };

        byte[] serializeData = JsonSerializer.SerializeToUtf8Bytes(response, s_jsonOptions);
        await _cache.SetAsync(request.CacheKey, serializeData, cacheOptions, cancellationToken);
        _logger.Info($"Cache add: {request.CacheKey}");

        if (request.CacheGroupKey != null)
        {
            await AddCacheKeyToGroup(request, slidingExpiration, cancellationToken);
        }

        return response;
    }

    private async Task AddCacheKeyToGroup(TRequest request, TimeSpan slidingExpiration, CancellationToken cancellationToken)
    {
        var cacheGroupTask = _cache.GetAsync(key: request.CacheGroupKey!, cancellationToken);
        var expirationTask = _cache.GetAsync(key: $"{request.CacheGroupKey}SlidingExpiration", cancellationToken);

        await Task.WhenAll(cacheGroupTask, expirationTask);

        byte[]? cacheGroupCache = cacheGroupTask.Result;
        byte[]? existingExpirationCache = expirationTask.Result;

        HashSet<string> cacheKeysInGroup;

        if (cacheGroupCache != null)
        {
            cacheKeysInGroup = JsonSerializer.Deserialize<HashSet<string>>(cacheGroupCache, s_jsonOptions)
                ?? new HashSet<string>(StringComparer.Ordinal);
            cacheKeysInGroup.Add(request.CacheKey);
        }
        else
        {
            cacheKeysInGroup = new HashSet<string>(StringComparer.Ordinal) { request.CacheKey };
        }

        byte[] newCacheGroupCache = JsonSerializer.SerializeToUtf8Bytes(cacheKeysInGroup, s_jsonOptions);

        int cacheGroupSlidingExpirationValue = (int)slidingExpiration.TotalSeconds;

        if (existingExpirationCache != null
            && int.TryParse(Utf8Encoding.GetString(existingExpirationCache), out int existingValue))
        {
            cacheGroupSlidingExpirationValue = Math.Max(cacheGroupSlidingExpirationValue, existingValue);
        }

        byte[] serializedExpirationData = Utf8Encoding.GetBytes(cacheGroupSlidingExpirationValue.ToString());

        DistributedCacheEntryOptions cacheOptions = new()
        {
            SlidingExpiration = TimeSpan.FromSeconds(cacheGroupSlidingExpirationValue)
        };

        var setGroupTask = _cache.SetAsync(key: request.CacheGroupKey!, newCacheGroupCache, cacheOptions, cancellationToken);
        var setExpirationTask = _cache.SetAsync(
            key: $"{request.CacheGroupKey}SlidingExpiration",
            serializedExpirationData,
            cacheOptions,
            cancellationToken);

        await Task.WhenAll(setGroupTask, setExpirationTask);

        _logger.Info($"Cache group: {request.CacheGroupKey}");
    }
}
