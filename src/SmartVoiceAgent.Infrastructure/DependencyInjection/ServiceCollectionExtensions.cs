using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models;
using SmartVoiceAgent.Infrastructure.Agent.Agents;
using SmartVoiceAgent.Infrastructure.Agent.Conf;
using SmartVoiceAgent.Infrastructure.Agent.Functions;
using SmartVoiceAgent.Infrastructure.Agent.Tools;
using SmartVoiceAgent.Infrastructure.Mcp;
using SmartVoiceAgent.Infrastructure.Services;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;
using SmartVoiceAgent.Infrastructure.Skills.External;
using SmartVoiceAgent.Infrastructure.Skills.Planning;
using System.ClientModel;

namespace SmartVoiceAgent.Infrastructure.Extensions;

/// <summary>
/// Extension methods for configuring Smart Voice Agent services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Smart Voice Agent services to the DI container
    /// </summary>
    public static IServiceCollection AddSmartVoiceAgent(this IServiceCollection services, IConfiguration configuration)
    {


        // Context and analytics services
        services.AddSingleton<ConversationContextManager>();

        // Configuration options
        services.Configure<GroupChatOptions>(configuration.GetSection("GroupChat"));

        services.Configure<McpOptions>(configuration.GetSection("McpOptions"));

        Console.WriteLine("✅ Smart Voice Agent services registered");



        //Migration

        services.Configure<AIServiceConfiguration>(
    configuration.GetSection("AIService"));

        services.AddSingleton<IChatClient>(sp =>
        {
            var config = configuration
                .GetSection("AIService")
                .Get<AIServiceConfiguration>()
                ?? throw new InvalidOperationException("AIService configuration is missing.");

            return config.Provider switch
            {
                var provider when IsOpenAICompatibleProvider(provider) => CreateOpenAICompatibleClient(config),
                _ => throw new NotSupportedException(
                    $"AI provider '{config.Provider}' is not supported.")
            };
        });

        services.AddSingleton<IAgentFactory, AgentFactory>();
        services.AddSingleton<IAgentRegistry, AgentRegistry>();

        services.AddSingleton<IAgentOrchestrator>(sp =>
        {
            var registry = sp.GetRequiredService<IAgentRegistry>();
            var logger = sp.GetRequiredService<ILogger<SmartAgentOrchestrator>>();
            var uiLogService = sp.GetRequiredService<IUiLogService>();
            return new SmartAgentOrchestrator(registry, logger, uiLogService);
        });

        services.AddSingleton<SystemAgentTools>();
        services.AddSingleton<TaskAgentTools>();
        services.AddSingleton<WebSearchAgentTools>();
        services.AddSingleton<FileAgentTools>();
        services.AddSingleton<CommunicationAgentTools>();
        services.AddSingleton<ClipboardTools>();
        services.AddSingleton<SystemInformationTools>();
        services.AddSingleton<ISkillExecutor, SystemAgentSkillExecutor>();
        services.AddSingleton<ISkillExecutor, FileSkillExecutor>();
        services.AddSingleton<ISkillExecutor, WebSearchSkillExecutor>();
        services.AddSingleton<ISkillExecutor, CommunicationSkillExecutor>();
        services.AddSingleton<ISkillExecutor, ClipboardSkillExecutor>();
        services.AddSingleton<ISkillExecutor, SystemInformationSkillExecutor>();
        services.AddScoped<ISkillExecutor, ShellSkillExecutor>();
        services.AddHttpClient<WebPageSkillExecutor>();
        services.AddScoped<ISkillExecutor>(sp => sp.GetRequiredService<WebPageSkillExecutor>());
        services.AddScoped<ISkillExecutor>(sp => new WindowContextSkillExecutor(sp));
        services.AddScoped<ISkillExecutor>(sp => new AccessibilitySkillExecutor(sp));
        services.AddScoped<ISkillExecutor>(sp =>
        {
            var registry = sp.GetRequiredService<ISkillRegistry>();
            var chatClient = new Lazy<IChatClient>(() =>
            {
                var chatConfig = configuration
                    .GetSection("AIService:Chat")
                    .Get<AIServiceConfiguration>();

                return IsUsableAiConfiguration(chatConfig)
                    ? CreateOpenAICompatibleClient(chatConfig!)
                    : sp.GetRequiredService<IChatClient>();
            });

            return new ExternalSkillExecutor(
                () => chatClient.Value,
                registry,
                () => sp.GetService<ISkillRuntimeContextProvider>(),
                () => sp.GetService<ISkillActionExecutor>(),
                () => sp.GetService<ISkillAuditLogService>(),
                () => ResolveExternalSkillModelId(configuration));
        });
        services.AddSingleton<ISkillPlannerService, ModelSkillPlannerService>();

        // Host control service (must be registered before hosted service)
        services.AddSingleton<VoiceAgentHostControlService>();
        services.AddSingleton<IVoiceAgentHostControl>(sp => sp.GetRequiredService<VoiceAgentHostControlService>());

        services.AddHostedService<VoiceAgentHostedService>();

        return services;
    }

    /// <summary>
    /// Adds additional agent function services
    /// </summary>
    public static IServiceCollection AddAgentFunctions<T>(this IServiceCollection services)
        where T : class, IAgentFunctions
    {
        services.AddScoped<T>();
        Console.WriteLine($"✅ Agent function service {typeof(T).Name} registered");
        return services;
    }
    static IChatClient CreateOpenAICompatibleClient(AIServiceConfiguration config)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(config.Endpoint)
        };

        var apiKey = string.IsNullOrWhiteSpace(config.ApiKey) && IsOllamaProvider(config.Provider)
            ? "ollama"
            : config.ApiKey;

        var client = new OpenAIClient(
            credential: new ApiKeyCredential(apiKey),
            options: options);

        return client.GetChatClient(config.ModelId).AsIChatClient();
    }

    private static bool IsOpenAICompatibleProvider(string provider)
    {
        return provider.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase)
            || provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
            || provider.Equals("OpenAICompatible", StringComparison.OrdinalIgnoreCase)
            || IsOllamaProvider(provider);
    }

    private static bool IsUsableAiConfiguration(AIServiceConfiguration? config)
    {
        return config is not null
            && IsOpenAICompatibleProvider(config.Provider)
            && !string.IsNullOrWhiteSpace(config.Endpoint)
            && !string.IsNullOrWhiteSpace(config.ModelId)
            && (IsOllamaProvider(config.Provider) || !string.IsNullOrWhiteSpace(config.ApiKey));
    }

    private static string ResolveExternalSkillModelId(IConfiguration configuration)
    {
        var chatConfig = configuration
            .GetSection("AIService:Chat")
            .Get<AIServiceConfiguration>();
        if (IsUsableAiConfiguration(chatConfig))
        {
            return chatConfig!.ModelId;
        }

        return configuration
            .GetSection("AIService")
            .Get<AIServiceConfiguration>()
            ?.ModelId ?? string.Empty;
    }

    private static bool IsOllamaProvider(string provider)
    {
        return provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase);
    }
}

