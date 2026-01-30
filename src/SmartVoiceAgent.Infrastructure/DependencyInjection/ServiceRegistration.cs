using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Factories;
using SmartVoiceAgent.Infrastructure.Helpers;
using SmartVoiceAgent.Infrastructure.Services;
using SmartVoiceAgent.Infrastructure.Services.ApplicationScanner;
using SmartVoiceAgent.Infrastructure.Services.Intent;
using SmartVoiceAgent.Infrastructure.Services.Language;
using SmartVoiceAgent.Infrastructure.Services.Stt;
using SmartVoiceAgent.Infrastructure.Services.System;
using SmartVoiceAgent.Infrastructure.Services.UiLog;
using SmartVoiceAgent.Infrastructure.Services.Message;
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
        services.AddSingleton<ISTTServiceFactory, STTServiceFactory>();
        services.AddSingleton<AudioProcessingService>();
        services.AddScoped<ILanguageDetectionService, HuggingFaceLanguageDetectionService>();
        services.AddSingleton<IApplicationScannerServiceFactory, ApplicationScannerFactory>();
        services.AddSingleton<IApplicationServiceFactory, ApplicationServiceFactory>();
        services.AddSingleton<IVoiceRecognitionFactory, VoiceRecognitionServiceFactory>();
        services.AddSingleton<IMusicServiceFactory, MusicServiceFactory>();

        // Register platform-specific services directly (using factories)
        services.AddSingleton<IApplicationService>(sp => 
            sp.GetRequiredService<IApplicationServiceFactory>().Create());
        services.AddSingleton<IMusicService>(sp => 
            sp.GetRequiredService<IMusicServiceFactory>().Create());
        services.AddSingleton<ICommandLearningService, CommandLearningService>();
        services.AddScoped<IWebResearchService, AiWebResearchService>();
        services.AddScoped<ICommandHandlerService, CommandHandlerService>();
        services.AddHttpClient();
        services.AddSingleton<OllamaSTTService>();
        services.AddSingleton<WhisperSTTService>();
        services.AddSingleton<HuggingFaceSTTService>();

        services.AddSingleton<IOcrService, OcrService>();

        services.AddSingleton<ISystemControlServiceFactory, SystemControlServiceFactory>();

        services.AddScoped<IntentDetectorService>(); // Original pattern-based service
        services.AddScoped<AiIntentDetectionService>();
        services.AddScoped<SemanticIntentDetectionService>();


        services.AddScoped<IScreenCaptureService, ScreenCaptureService>();
        services.AddScoped<IOcrService, OcrService>();
        services.AddScoped<IActiveWindowService, ActiveWindowService>();
        services.AddScoped<IScreenContextService, ScreenContextService>();


        // Register context-aware service with proper dependencies
        services.AddScoped<ContextAwareIntentDetectionService>();

        // Register the hybrid service as the main implementation
        services.AddScoped<IIntentDetectionService, HybridIntentDetectionService>();

        // Register UI Log Service (must be set externally by UI)
        services.AddSingleton<IUiLogService>(sp => 
        {
            // This will be replaced by the UI's implementation
            // We return a dummy implementation that just writes to console
            return new ConsoleUiLogService();
        });

        // Add UI Logger Provider
        services.AddSingleton<ILoggerProvider>(sp =>
        {
            var uiLogService = sp.GetRequiredService<IUiLogService>();
            return new UiLogLoggerProvider(uiLogService);
        });

        // Register Command Input Service for UI-to-Agent communication
        services.AddSingleton<ICommandInputService, CommandInputService>();
        
        // Register Message Services (Email, SMS, etc.)
        services.AddScoped<IMessageServiceFactory, MessageServiceFactory>();

        return services;
    }
}
