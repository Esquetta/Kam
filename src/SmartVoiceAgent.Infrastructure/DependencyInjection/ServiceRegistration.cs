using Core.CrossCuttingConcerns.Logging.Serilog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Factories;
using SmartVoiceAgent.Infrastructure.Helpers;
using SmartVoiceAgent.Infrastructure.Mcp;
using SmartVoiceAgent.Infrastructure.Services;
using SmartVoiceAgent.Infrastructure.Services.ApplicationScanner;
using SmartVoiceAgent.Infrastructure.Services.Intent;
using SmartVoiceAgent.Infrastructure.Services.Language;
using SmartVoiceAgent.Infrastructure.Services.Stt;
using SmartVoiceAgent.Infrastructure.Services.WebResearch;

namespace SmartVoiceAgent.Infrastructure.DependencyInjection;

/// <summary>
/// Service registrations for Infrastructure layer.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<ICommandLearningService, CommandLearningService>();
        services.AddHostedService<AgentHostedService>();
        services.AddSingleton<ISTTServiceFactory, STTServiceFactory>();
        services.AddSingleton<AudioProcessingService>();
        services.AddScoped<ILanguageDetectionService, HuggingFaceLanguageDetectionService>();
        services.AddSingleton<IApplicationScannerServiceFactory, ApplicationScannerFactory>();
        services.AddSingleton<IApplicationServiceFactory, ApplicationServiceFactory>();
        services.AddSingleton<IVoiceRecognitionFactory, VoiceRecognitionServiceFactory>();
        services.AddSingleton<IMusicServiceFactory, MusicServiceFactory>();
        services.AddSingleton<ICommandLearningService, CommandLearningService>();
        services.AddScoped<IWebResearchService, AiWebResearchService>();
        services.AddScoped<ICommandHandlerService, CommandHandlerService>();
        services.AddHttpClient();
        services.AddSingleton<OllamaSTTService>();
        services.AddSingleton<WhisperSTTService>();
        services.AddSingleton<HuggingFaceSTTService>();
        services.AddScoped<Functions>();
        services.Configure<McpOptions>(configuration.GetSection("MCP"));



        services.AddScoped<IntentDetectorService>(); // Original pattern-based service
        services.AddScoped<AiIntentDetectionService>();
        services.AddScoped<SemanticIntentDetectionService>();
        services.AddScoped<ConversationContextManager>();

        // Register context-aware service with proper dependencies
        services.AddScoped<ContextAwareIntentDetectionService>();

        // Register the hybrid service as the main implementation
        services.AddScoped<IIntentDetectionService,HybridIntentDetectionService>();

        return services;
    }
}
