using Core.CrossCuttingConcerns.Logging.Serilog;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using System.Text;
using System.Text.Json;

namespace SmartVoiceAgent.Application.Pipelines.Caching
{
    public class CacheRemovingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, ICacheRemoverRequest
    {
        private readonly IDistributedCache _cache;
        private readonly LoggerServiceBase _logger;

        public CacheRemovingBehavior(IDistributedCache cache, LoggerServiceBase logger)
        {
            _cache = cache;
            _logger = logger;
        }
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            if (request.BypassCache)
                return await next();

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
}
