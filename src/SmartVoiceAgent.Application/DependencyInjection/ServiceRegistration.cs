using Core.CrossCuttingConcerns.Logging.Serilog;
using Core.CrossCuttingConcerns.Logging.Serilog.Logger;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Application.Behaviors.Logging;
using SmartVoiceAgent.Application.Behaviors.Performance;
using SmartVoiceAgent.Application.Behaviors.Validation;
using SmartVoiceAgent.Application.Pipelines.Caching;
using System.Reflection;

namespace SmartVoiceAgent.Application.DependencyInjection;

/// <summary>
/// Provides extension methods for registering application services.
/// </summary>
/// 
public static class ServiceRegistration
{
    /// <summary>
    /// Registers application layer services and handlers.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The updated service collection.</returns>

    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddCortexMediator(new[] { typeof(ServiceRegistration) }, options =>
        {
            options.AddOpenCommandPipelineBehavior(typeof(RequestValidationBehavior<,>));
            options.AddOpenCommandPipelineBehavior(typeof(LoggingBehavior<,>));
            options.AddOpenCommandPipelineBehavior(typeof(PerformanceBehavior<,>));
            options.AddOpenCommandPipelineBehavior(typeof(CachingBehavior<,>));
            options.AddOpenCommandPipelineBehavior(typeof(CacheRemovingBehavior<,>));

            options.AddOpenQueryPipelineBehavior(typeof(RequestValidationQueryBehavior<,>));
            options.AddOpenQueryPipelineBehavior(typeof(LoggingQueryBehavior<,>));
            options.AddOpenQueryPipelineBehavior(typeof(PerformanceQueryBehavior<,>));
            options.AddOpenQueryPipelineBehavior(typeof(QueryCachingBehavior<,>));
            options.AddOpenQueryPipelineBehavior(typeof(QueryCacheRemovingBehavior<,>));
        });
        services.AddDistributedMemoryCache();


        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        services.AddSingleton<LoggerServiceBase, MongoDbLogger>();

        return services;
    }
}
