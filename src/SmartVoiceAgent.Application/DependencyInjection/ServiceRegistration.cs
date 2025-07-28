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
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
            cfg.AddOpenBehavior(typeof(RequestValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>));
            cfg.AddOpenBehavior(typeof(CachingBehavior<,>));
            cfg.AddOpenBehavior(typeof(CacheRemovingBehavior<,>));
        });
        services.AddDistributedMemoryCache();
        services.AddScoped<Functions>();

        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        services.AddSingleton<LoggerServiceBase, MongoDbLogger>();

        return services;
    }
}