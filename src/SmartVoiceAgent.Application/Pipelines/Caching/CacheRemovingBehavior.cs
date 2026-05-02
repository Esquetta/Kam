using Core.CrossCuttingConcerns.Logging.Serilog;
using Microsoft.Extensions.Caching.Distributed;
using System.Text;
using System.Text.Json;

namespace SmartVoiceAgent.Application.Pipelines.Caching;

public class CacheRemovingBehavior<TRequest, TResponse> :
    ICommandPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>, ICacheRemoverRequest
{
    private readonly IDistributedCache _cache;
    private readonly LoggerServiceBase _logger;

    public CacheRemovingBehavior(IDistributedCache cache, LoggerServiceBase logger)
    {
        _cache = cache;
        _logger = logger;
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

        TResponse response = await next();

        if (request.CacheGroupKey != null)
        {
            byte[]? cachedGroup = await _cache.GetAsync(request.CacheGroupKey, cancellationToken);
            if (cachedGroup != null)
            {
                HashSet<string> keysInGroup = JsonSerializer.Deserialize<HashSet<string>>(Encoding.Default.GetString(cachedGroup))!;
                foreach (string key in keysInGroup)
                {
                    await _cache.RemoveAsync(key, cancellationToken);
                    _logger.Info($"Removed Cache -> {key}");
                }

                await _cache.RemoveAsync(request.CacheGroupKey, cancellationToken);
                _logger.Info($"Removed Cache -> {request.CacheGroupKey}");
                await _cache.RemoveAsync(key: $"{request.CacheGroupKey}SlidingExpiration", cancellationToken);
                _logger.Info($"Removed Cache -> {request.CacheGroupKey}SlidingExpiration");
            }
        }

        if (request.CacheKey != null)
        {
            await _cache.RemoveAsync(request.CacheKey, cancellationToken);
            _logger.Info($"Removed Cache -> {request.CacheKey}");
        }

        return response;
    }
}

public class QueryCacheRemovingBehavior<TRequest, TResponse> :
    IQueryPipelineBehavior<TRequest, TResponse>
    where TRequest : IQuery<TResponse>, ICacheRemoverRequest
{
    private readonly CacheRemovingBehaviorCore<TRequest, TResponse> _core;

    public QueryCacheRemovingBehavior(IDistributedCache cache, LoggerServiceBase logger)
    {
        _core = new CacheRemovingBehaviorCore<TRequest, TResponse>(cache, logger);
    }

    public Task<TResponse> Handle(TRequest request, QueryHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        return _core.HandleCore(request, next.Invoke, cancellationToken);
    }
}

internal sealed class CacheRemovingBehaviorCore<TRequest, TResponse>
    where TRequest : ICacheRemoverRequest
{
    private readonly IDistributedCache _cache;
    private readonly LoggerServiceBase _logger;

    public CacheRemovingBehaviorCore(IDistributedCache cache, LoggerServiceBase logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<TResponse> HandleCore(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
    {
        if (request.BypassCache)
        {
            return await next();
        }

        TResponse response = await next();

        if (request.CacheGroupKey != null)
        {
            byte[]? cachedGroup = await _cache.GetAsync(request.CacheGroupKey, cancellationToken);
            if (cachedGroup != null)
            {
                HashSet<string> keysInGroup = JsonSerializer.Deserialize<HashSet<string>>(Encoding.Default.GetString(cachedGroup))!;
                foreach (string key in keysInGroup)
                {
                    await _cache.RemoveAsync(key, cancellationToken);
                    _logger.Info($"Removed Cache -> {key}");
                }

                await _cache.RemoveAsync(request.CacheGroupKey, cancellationToken);
                _logger.Info($"Removed Cache -> {request.CacheGroupKey}");
                await _cache.RemoveAsync(key: $"{request.CacheGroupKey}SlidingExpiration", cancellationToken);
                _logger.Info($"Removed Cache -> {request.CacheGroupKey}SlidingExpiration");
            }
        }

        if (request.CacheKey != null)
        {
            await _cache.RemoveAsync(request.CacheKey, cancellationToken);
            _logger.Info($"Removed Cache -> {request.CacheKey}");
        }

        return response;
    }
}
