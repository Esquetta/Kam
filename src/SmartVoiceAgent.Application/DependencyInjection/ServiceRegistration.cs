using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Application.Handlers.Commands;
using SmartVoiceAgent.Application.Handlers.Queries;
using SmartVoiceAgent.Application.Queries;
using SmartVoiceAgent.Core.Commands;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;

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
        services.AddScoped<ICommandHandler<OpenApplicationCommand, CommandResultDTO>, OpenApplicationCommandHandler>();

        services.AddScoped<IQueryHandler<GetAppStatusQuery, AppStatus>, GetApplicationStatusQueryHandler>();


        return services;
    }
}