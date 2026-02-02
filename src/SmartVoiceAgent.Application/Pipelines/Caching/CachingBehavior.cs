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

        // Performance: Reuse JsonSerializerOptions instance
        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public CachingBehavior(IDistributedCache cache, LoggerServiceBase logger, IConfiguration configuration)
        {
            _cache = cache;
            _logger = logger;
            // Performance: Cache settings loaded once during construction, not per request
            _cacheSettings = configuration.GetSection("CacheSettings").Get<CacheSettings>() 
                ?? new CacheSettings { SlidingExpiration = 1 }; // Default fallback
        }

        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            if (request.BypassCache)
                return await next();

            byte[]? cachedResponse = await _cache.GetAsync(request.CacheKey, cancellationToken);
            if (cachedResponse != null)
            {
                // Performance: Use cached JsonSerializerOptions
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
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken
        )
        {
            TResponse response = await next();

            TimeSpan slidingExpiration = request.SlidingExpiration 
                ?? TimeSpan.FromDays(_cacheSettings.SlidingExpiration);
            
            DistributedCacheEntryOptions cacheOptions = new() { SlidingExpiration = slidingExpiration };

            // Performance: Serialize directly to UTF-8 bytes to avoid string allocation
            byte[] serializeData = JsonSerializer.SerializeToUtf8Bytes(response, s_jsonOptions);
            await _cache.SetAsync(request.CacheKey, serializeData, cacheOptions, cancellationToken);
            _logger.Info($"Cache add: {request.CacheKey}");

            if (request.CacheGroupKey != null)
                await AddCacheKeyToGroup(request, slidingExpiration, cancellationToken);

            return response;
        }

        private async Task AddCacheKeyToGroup(TRequest request, TimeSpan slidingExpiration, CancellationToken cancellationToken)
        {
            // Performance: Run cache group operations in parallel when possible
            var cacheGroupTask = _cache.GetAsync(key: request.CacheGroupKey!, cancellationToken);
            var expirationTask = _cache.GetAsync(
                key: $"{request.CacheGroupKey}SlidingExpiration",
                cancellationToken
            );

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
            
            if (existingExpirationCache != null)
            {
                // Parse existing expiration and use the larger value
                if (int.TryParse(Utf8Encoding.GetString(existingExpirationCache), out int existingValue))
                {
                    cacheGroupSlidingExpirationValue = Math.Max(cacheGroupSlidingExpirationValue, existingValue);
                }
            }

            byte[] serializedExpirationData = Utf8Encoding.GetBytes(cacheGroupSlidingExpirationValue.ToString());

            DistributedCacheEntryOptions cacheOptions = new() 
            { 
                SlidingExpiration = TimeSpan.FromSeconds(cacheGroupSlidingExpirationValue) 
            };

            // Performance: Set both cache entries in parallel
            var setGroupTask = _cache.SetAsync(key: request.CacheGroupKey!, newCacheGroupCache, cacheOptions, cancellationToken);
            var setExpirationTask = _cache.SetAsync(
                key: $"{request.CacheGroupKey}SlidingExpiration",
                serializedExpirationData,
                cacheOptions,
                cancellationToken
            );

            await Task.WhenAll(setGroupTask, setExpirationTask);
            
            _logger.Info($"Cache group: {request.CacheGroupKey}");
        }
    }
}
