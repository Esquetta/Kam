using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using SmartVoiceAgent.Application.Agent;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models;
using SmartVoiceAgent.Infrastructure.Agent.Agents;
using SmartVoiceAgent.Infrastructure.Agent.Conf;
using SmartVoiceAgent.Infrastructure.Agent.Functions;
using SmartVoiceAgent.Infrastructure.Agent.Tools;
using SmartVoiceAgent.Infrastructure.Mcp;
using SmartVoiceAgent.Infrastructure.Services;
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
        // Core agent function services
        services.AddScoped<SystemAgentFunctions>();
        services.AddScoped<WebSearchAgentFunctions>();

        // Context and analytics services
        services.AddSingleton<ConversationContextManager>();
        services.AddSingleton<GroupChatAnalytics>();

        services.BuildServiceProvider();

        // Configuration options
        services.Configure<GroupChatOptions>(configuration.GetSection("GroupChat"));

        services.AddScoped<Functions>();
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
                "OpenRouter" => CreateOpenRouterClient(config),

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
            return new SmartAgentOrchestrator(registry, logger);
        });

        services.AddSingleton<SystemAgentTools>();
        services.AddSingleton<TaskAgentTools>();
        services.AddSingleton<WebSearchAgentTools>();

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
    static IChatClient CreateOpenRouterClient(AIServiceConfiguration config)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(config.Endpoint)
        };

        var client = new OpenAIClient(
            credential: new ApiKeyCredential(config.ApiKey),
            options: options);

        return client.GetChatClient(config.ModelId).AsIChatClient();
    }
}

