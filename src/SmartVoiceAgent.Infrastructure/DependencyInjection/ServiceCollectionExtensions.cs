using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models;
using SmartVoiceAgent.Core.Models.CodingAgent;
using SmartVoiceAgent.Core.Models.GitHub;
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
using Microsoft.Extensions.Options;

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
        services.Configure<CodingAgentOptions>(configuration.GetSection(CodingAgentOptions.SectionName));
        services.Configure<GitHubAppOptions>(configuration.GetSection(GitHubAppOptions.SectionName));

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

            return CreateObservedChatClient(sp, config);
        });

        services.AddSingleton<IAgentFactory>(sp =>
        {
            var agentConfig = ResolveAgentModelConfiguration(configuration);
            var chatClient = IsUsableAiConfiguration(agentConfig)
                ? CreateObservedChatClient(sp, agentConfig!)
                : sp.GetRequiredService<IChatClient>();

            return new AgentFactory(
                chatClient,
                sp,
                sp.GetRequiredService<ILogger<AgentFactory>>());
        });
        services.AddSingleton<IRuntimeAgentFactory>(sp =>
        {
            var agentConfig = ResolveAgentModelConfiguration(configuration);
            var chatClient = new Lazy<IChatClient>(() =>
                IsUsableAiConfiguration(agentConfig)
                    ? CreateObservedChatClient(sp, agentConfig!)
                    : sp.GetRequiredService<IChatClient>());

            return new RuntimeAgentFactory(
                () => chatClient.Value,
                sp.GetRequiredService<ILogger<RuntimeAgentFactory>>(),
                sp.GetRequiredService<IRuntimeAgentRunStore>(),
                sp.GetService<IRuntimeAgentReadOnlyToolService>(),
                sp.GetService<IUiLogService>(),
                agentConfig?.ModelId ?? string.Empty);
        });
        services.AddSingleton<IRuntimeAgentRunStore, InMemoryRuntimeAgentRunStore>();
        services.AddSingleton<IRuntimeAgentReadOnlyToolService, FileRuntimeAgentReadOnlyToolService>();
        services.AddSingleton<IAgentRegistry, AgentRegistry>();

        services.AddSingleton<IAgentOrchestrator>(sp =>
        {
            var registry = sp.GetRequiredService<IAgentRegistry>();
            var logger = sp.GetRequiredService<ILogger<SmartAgentOrchestrator>>();
            var uiLogService = sp.GetRequiredService<IUiLogService>();
            return new SmartAgentOrchestrator(registry, logger, uiLogService);
        });

        services.AddScoped<SystemAgentTools>();
        services.AddSingleton<TaskAgentTools>();
        services.AddScoped<WebSearchAgentTools>();
        services.AddSingleton(sp =>
        {
            var codingOptions = sp.GetRequiredService<IOptions<CodingAgentOptions>>().Value;
            var workspaceRoot = codingOptions.IsEnabled
                ? codingOptions.GetWorkspaceRootOrDefault()
                : null;

            return new FileAgentTools(workspaceRoot);
        });
        services.AddScoped<CommunicationAgentTools>();
        services.AddSingleton<ClipboardTools>();
        services.AddSingleton<SystemInformationTools>();
        services.AddScoped<ISkillExecutor, SystemAgentSkillExecutor>();
        services.AddSingleton<ISkillExecutor, FileSkillExecutor>();
        services.AddScoped<ISkillExecutor, WebSearchSkillExecutor>();
        services.AddScoped<ISkillExecutor, CommunicationSkillExecutor>();
        services.AddSingleton<ISkillExecutor, ClipboardSkillExecutor>();
        services.AddSingleton<ISkillExecutor, SystemInformationSkillExecutor>();
        services.AddScoped<ISkillExecutor, ShellSkillExecutor>();
        services.AddScoped<ISkillExecutor, RuntimeAgentSkillExecutor>();
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
                    ? CreateObservedChatClient(sp, chatConfig!)
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
    static IChatClient CreateChatClient(AIServiceConfiguration config)
    {
        return config.Provider switch
        {
            var provider when IsAnthropicProvider(provider) => CreateAnthropicClient(config),
            var provider when IsOpenAICompatibleProvider(provider) => CreateOpenAICompatibleClient(config),
            _ => throw new NotSupportedException(
                $"AI provider '{config.Provider}' is not supported.")
        };
    }

    static IChatClient CreateObservedChatClient(IServiceProvider sp, AIServiceConfiguration config)
    {
        return new AiProviderAlertingChatClient(
            CreateChatClient(config),
            sp.GetService<IUiLogService>(),
            config.ModelId);
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

    static IChatClient CreateAnthropicClient(AIServiceConfiguration config)
    {
        var client = new AnthropicClient().WithOptions(options =>
        {
            options.ApiKey = config.ApiKey;
            if (!string.IsNullOrWhiteSpace(config.Endpoint))
            {
                options.BaseUrl = config.Endpoint.TrimEnd('/');
            }

            return options;
        });

        var defaultMaxTokens = config.DefaultMaxTokens > 0
            ? config.DefaultMaxTokens
            : (int?)null;

        return client.AsIChatClient(config.ModelId, defaultMaxTokens);
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
            && IsSupportedAiProvider(config.Provider)
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

    private static AIServiceConfiguration? ResolveAgentModelConfiguration(IConfiguration configuration)
    {
        var agentConfig = configuration
            .GetSection("AIService:Agents")
            .Get<AIServiceConfiguration>();
        if (IsUsableAiConfiguration(agentConfig))
        {
            return agentConfig;
        }

        var chatConfig = configuration
            .GetSection("AIService:Chat")
            .Get<AIServiceConfiguration>();
        if (IsUsableAiConfiguration(chatConfig))
        {
            return chatConfig;
        }

        return configuration
            .GetSection("AIService")
            .Get<AIServiceConfiguration>();
    }

    private static bool IsOllamaProvider(string provider)
    {
        return provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAnthropicProvider(string provider)
    {
        return provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedAiProvider(string provider)
    {
        return IsOpenAICompatibleProvider(provider) || IsAnthropicProvider(provider);
    }
}

