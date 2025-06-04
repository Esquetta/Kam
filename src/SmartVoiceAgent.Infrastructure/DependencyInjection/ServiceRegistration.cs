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
        services.AddScoped<IApplicationScanner, ApplicationScannerService>();
        services.AddScoped<IIntentDetector, IntentDetectorService>();
        services.AddScoped<ICommandLearningService, CommandLearningService>();
        services.AddScoped<IVoiceRecognitionService, VoiceRecognitionService>();
        services.AddScoped<ICommandBus, CommandBus>();
        services.AddScoped<IQueryBus, QueryBus>();
        services.AddScoped<IMusicService, MusicService>();
        services.AddHostedService<AgentHostedService>();

        return services;
    }
}
