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

        // Agent factory service
        services.AddScoped<IGroupChatFactory, GroupChatFactory>();

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

/// <summary>
/// Factory interface for creating group chats
/// </summary>
public interface IGroupChatFactory
{
    Task<SmartGroupChat> CreateGroupChatAsync(
        string apiKey,
        string model,
        string endpoint,
        GroupChatOptions options = null);
}

/// <summary>
/// Factory implementation for creating group chats with DI
/// </summary>
public class GroupChatFactory : IGroupChatFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    public GroupChatFactory(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    public async Task<SmartGroupChat> CreateGroupChatAsync(
        string apiKey,
        string model,
        string endpoint,
        GroupChatOptions options = null)
    {
        return await GroupChatAgentFactory.CreateGroupChatAsync(
            apiKey: apiKey,
            model: model,
            endpoint: endpoint,
            serviceProvider: _serviceProvider,
            configuration: _configuration,
            options: options);
    }
}