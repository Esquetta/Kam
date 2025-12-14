using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.Configure<McpOptions>(configuration.GetSection("MCP"));

        Console.WriteLine("✅ Smart Voice Agent services registered");



        //Migration

        services.Configure<AgentConfiguration>(
                configuration.GetSection("AIService"));

        services.AddSingleton<IChatClient>(sp =>
        {
            var config = configuration.GetSection("AIService")
                .Get<AgentConfiguration>() ?? new();

            return config.Provider switch
            {
                "OpenRouter" => new OpenAIClient(credential: new ApiKeyCredential(config.ApiKey), options: new OpenAIClientOptions({
                    Endpoint = new Uri(config.Endpoint)
                }).GetChatClient(config.ModelId)
            };
        });

        services.AddSingleton<IAgentRegistry, AgentRegistry>();
        services.AddSingleton<IAgentFactory, AgentFactory>();
        services.AddSingleton<IAgentOrchestrator, SmartAgentOrchestrator>();

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
}

