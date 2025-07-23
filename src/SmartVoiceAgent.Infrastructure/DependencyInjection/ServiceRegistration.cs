using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Factories;
using SmartVoiceAgent.Infrastructure.Helpers;
using SmartVoiceAgent.Infrastructure.Services;
using SmartVoiceAgent.Infrastructure.Services.ApplicationScanner;
using SmartVoiceAgent.Infrastructure.Services.Language;
using SmartVoiceAgent.Infrastructure.Services.Stt;
using SmartVoiceAgent.Infrastructure.Services.WebResearch;

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
        services.AddSingleton<IApplicationScannerServiceFactory,ApplicationScannerFactory>();
        services.AddSingleton<IApplicationServiceFactory,ApplicationServiceFactory>();
        services.AddSingleton<IVoiceRecognitionFactory,VoiceRecognitionServiceFactory>();
        services.AddSingleton<IMusicServiceFactory,MusicServiceFactory>();
        services.AddSingleton<ICommandLearningService,CommandLearningService>();
        services.AddScoped<IWebResearchService, WebResearchService>();




        return services;
    }
}
