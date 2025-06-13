using Core.CrossCuttingConcerns.Logging.Serilog;
using Core.CrossCuttingConcerns.Logging.Serilog.Logger;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Application.Behaviors.Logging;
using SmartVoiceAgent.Application.Behaviors.Performance;
using SmartVoiceAgent.Application.Behaviors.Validation;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Application.Handlers;
using SmartVoiceAgent.Application.Handlers.Commands;
using SmartVoiceAgent.Application.Handlers.Queries;
using SmartVoiceAgent.Application.Pipelines.Caching;
using SmartVoiceAgent.Application.Queries;
using SmartVoiceAgent.Core.Commands;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;
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

        services.AddScoped<ICommandHandler<OpenApplicationCommand, CommandResultDTO>, OpenApplicationCommandHandler>();
        services.AddScoped<ICommandHandler<ControlDeviceCommand, CommandResultDTO>, ControlDeviceCommandHandler>();
        services.AddScoped<ICommandHandler<PlayMusicCommand, CommandResultDTO>, PlayMusicCommandHandler>();
        services.AddScoped<ICommandHandler<SearchWebCommand, CommandResultDTO>, SearchWebCommandHandler>();
        services.AddScoped<ICommandHandler<SendMessageCommand, CommandResultDTO>, SendMessageCommandHandler>();

        services.AddScoped<IQueryHandler<GetApplicationStatusQuery, AppStatus>, GetApplicationStatusQueryHandler>();
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ServiceRegistration).Assembly));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddSingleton<LoggerServiceBase, MongoDbLogger>();
        return services;
    }
}