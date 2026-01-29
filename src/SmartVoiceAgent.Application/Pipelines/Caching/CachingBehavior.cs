using Core.CrossCuttingConcerns.Logging.Serilog;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;

namespace SmartVoiceAgent.Application.Pipelines.Caching
{
    public class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, ICachableRequest
    {
        private readonly IDistributedCache _cache;
        private readonly CacheSettings _cacheSettings;
        private readonly LoggerServiceBase _logger;
        private static readonly UTF8Encoding Utf8Encoding = new UTF8Encoding(false);

        public CachingBehavior(IDistributedCache cache, LoggerServiceBase logger, IConfiguration configuration)
        {
            _cache = cache;
            _logger = logger;
            _cacheSettings = configuration.GetSection("CacheSettings").Get<CacheSettings>() 
                ?? throw new InvalidOperationException("CacheSettings not found in configuration");
        }

        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            if (request.BypassCache)
                return await next();

            byte[]? cachedResponse = await _cache.GetAsync(request.CacheKey, cancellationToken);
            if (cachedResponse != null)
            {
                // Use UTF8 encoding consistently instead of Encoding.Default
                TResponse? response = JsonSerializer.Deserialize<TResponse>(cachedResponse);
                if (response != null)
                {
                    _logger.Info($"Fetched from Cache -> {request.CacheKey}");
                    return response;
                }
            }

            return await GetResponseAndAddToCache(request, next, cancellationToken);
        }

        private async Task<TResponse> GetResponseAndAddToCache(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken
        )
        {
            TResponse response = await next();

            TimeSpan slidingExpiration = request.SlidingExpiration 
                ?? TimeSpan.FromDays(_cacheSettings.SlidingExpiration);
            
            DistributedCacheEntryOptions cacheOptions = new() { SlidingExpiration = slidingExpiration };

            // Serialize directly to UTF-8 bytes to avoid string allocation
            byte[] serializeData = JsonSerializer.SerializeToUtf8Bytes(response);
            await _cache.SetAsync(request.CacheKey, serializeData, cacheOptions, cancellationToken);
            _logger.Info($"Added to Cache -> {request.CacheKey}");

            if (request.CacheGroupKey != null)
                await AddCacheKeyToGroup(request, slidingExpiration, cancellationToken);

            return response;
        }

        private async Task AddCacheKeyToGroup(TRequest request, TimeSpan slidingExpiration, CancellationToken cancellationToken)
        {
            byte[]? cacheGroupCache = await _cache.GetAsync(key: request.CacheGroupKey!, cancellationToken);
            HashSet<string> cacheKeysInGroup;
            
            if (cacheGroupCache != null)
            {
                // Use UTF8 encoding consistently
                cacheKeysInGroup = JsonSerializer.Deserialize<HashSet<string>>(cacheGroupCache) 
                    ?? new HashSet<string>();
                cacheKeysInGroup.Add(request.CacheKey);
            }
            else
            {
                cacheKeysInGroup = new HashSet<string>(StringComparer.Ordinal) { request.CacheKey };
            }
            
            byte[] newCacheGroupCache = JsonSerializer.SerializeToUtf8Bytes(cacheKeysInGroup);

            int cacheGroupSlidingExpirationValue = (int)slidingExpiration.TotalSeconds;
            
            byte[]? existingExpirationCache = await _cache.GetAsync(
                key: $"{request.CacheGroupKey}SlidingExpiration",
                cancellationToken
            );
            
            if (existingExpirationCache != null)
            {
                // Parse existing expiration and use the larger value
                if (int.TryParse(Utf8Encoding.GetString(existingExpirationCache), out int existingValue))
                {
                    cacheGroupSlidingExpirationValue = Math.Max(cacheGroupSlidingExpirationValue, existingValue);
                }
            }

            byte[] serializedExpirationData = JsonSerializer.SerializeToUtf8Bytes(cacheGroupSlidingExpirationValue);

            DistributedCacheEntryOptions cacheOptions = new() 
            { 
                SlidingExpiration = TimeSpan.FromSeconds(cacheGroupSlidingExpirationValue) 
            };

            await _cache.SetAsync(key: request.CacheGroupKey!, newCacheGroupCache, cacheOptions, cancellationToken);
            _logger.Info($"Added to Cache -> {request.CacheGroupKey}");

            await _cache.SetAsync(
                key: $"{request.CacheGroupKey}SlidingExpiration",
                serializedExpirationData,
                cacheOptions,
                cancellationToken
            );
            _logger.Info($"Added to Cache -> {request.CacheGroupKey}SlidingExpiration");
        }
    }
}
