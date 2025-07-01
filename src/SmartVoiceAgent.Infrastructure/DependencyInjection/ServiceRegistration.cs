using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Factories;
using SmartVoiceAgent.Infrastructure.Services;

namespace SmartVoiceAgent.Infrastructure.DependencyInjection;

/// <summary>
/// Service registrations for Infrastructure layer.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddScoped<IApplicationScanner, ApplicationScannerService>();
        services.AddScoped<IIntentDetector, IntentDetectorService>();
        services.AddScoped<ICommandLearningService, CommandLearningService>();
        services.AddScoped<IVoiceRecognitionService, VoiceRecognitionService>();
        services.AddScoped<ICommandBus, CommandBus>();
        services.AddScoped<IQueryBus, QueryBus>();
        services.AddHostedService<AgentHostedService>();
        services.AddSingleton<IApplicationServiceFactory, ApplicationServiceFactory>();
        services.AddSingleton<IVoiceRecognitionFactory, VoiceRecognitionServiceFactory>();
        services.AddSingleton<IMusicServiceFactory, MusicServiceFactory>();




        return services;
    }
}
