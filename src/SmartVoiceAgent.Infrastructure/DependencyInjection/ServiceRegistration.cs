using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Factories;
using SmartVoiceAgent.Infrastructure.Helpers;
using SmartVoiceAgent.Infrastructure.Services;
using SmartVoiceAgent.Infrastructure.Services.ApplicationScanner;
using SmartVoiceAgent.Infrastructure.Services.Language;
using SmartVoiceAgent.Infrastructure.Services.Stt;

namespace SmartVoiceAgent.Infrastructure.DependencyInjection;

/// <summary>
/// Service registrations for Infrastructure layer.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddScoped<IIntentDetectionService, IntentDetectorService>();
        services.AddScoped<ICommandLearningService, CommandLearningService>();
        services.AddScoped<ICommandBus, CommandBus>();
        services.AddScoped<IQueryBus, QueryBus>();
        services.AddHostedService<AgentHostedService>();
        services.AddSingleton<IIntentDetectionService, IntentDetectorService>();
        services.AddSingleton<AudioProcessingService>();
        services.AddHttpClient<HuggingFaceSTTService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
            client.DefaultRequestHeaders.Add("User-Agent", "SmartVoiceAgent/1.0");
        });
        services.AddTransient<HuggingFaceSTTService>();
        services.AddSingleton<ISTTServiceFactory, STTServiceFactory>();
        services.AddScoped<ILanguageDetectionService, HuggingFaceLanguageDetectionService>();
        services.AddSingleton<IApplicationScanner>(provider => ApplicationScannerFactory.Create());
        services.AddSingleton<IApplicationService>(provider => ApplicationServiceFactory.Create());
        services.AddSingleton<IVoiceRecognitionService>(proivder => VoiceRecognitionServiceFactory.Create());
        services.AddSingleton<IMusicService>(proivder => MusicServiceFactory.Create());





        return services;
    }
}
