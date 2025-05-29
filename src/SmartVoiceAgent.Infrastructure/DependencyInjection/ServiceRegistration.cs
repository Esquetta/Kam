using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Services;

namespace SmartVoiceAgent.Infrastructure.DependencyInjection;

/// <summary>
/// Service registrations for Infrastructure layer.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddScoped<IApplicationService, ApplicationService>();
        services.AddScoped<IVoiceRecognitionService, VoiceRecognitionService>();

        return services;
    }
}
