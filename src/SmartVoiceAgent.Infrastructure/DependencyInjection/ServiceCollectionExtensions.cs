using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Application.Agent;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models;
using SmartVoiceAgent.Infrastructure.Agent;
using SmartVoiceAgent.Infrastructure.Agent.Functions;
using SmartVoiceAgent.Infrastructure.Mcp;

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

