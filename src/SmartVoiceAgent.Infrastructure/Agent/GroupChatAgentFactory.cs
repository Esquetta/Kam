using AutoGen;
using AutoGen.Core;
using AutoGen.OpenAI;
using AutoGen.OpenAI.Extension;
using AutoGen.SemanticKernel;
using AutoGen.SemanticKernel.Extension;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI;
using OpenAI.Chat;
using SmartVoiceAgent.Application.Agent;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Models;
using SmartVoiceAgent.Infrastructure.Agent;
using SmartVoiceAgent.Infrastructure.Agent.Functions;
using SmartVoiceAgent.Infrastructure.Mcp;
using System.ClientModel;

/// <summary>
/// Advanced Group Chat System with Context Management, Memory, and Analytics
/// </summary>
public static class GroupChatAgentFactory
{

    public static async Task<SmartGroupChat> CreateGroupChatAsync(
    string apiKey,
    string model,
    IServiceProvider serviceProvider,
    string endpoint,
    IConfiguration configuration,
    GroupChatOptions options = null)
    {
        options ??= new GroupChatOptions();
        var mcpOptions = new McpOptions();
        configuration.GetSection("Mcpverse").Bind(mcpOptions);
        Console.WriteLine("🏗️ Building Advanced Group Chat System with Intent-Based Routing...");
        options.EnableAnalyticsAgent = true;
        options.EnableWebSearchAgent = true;
        options.EnableContextMemory = true;
        // Create context-aware agents
        var contextManager = new ConversationContextManager();
        var analytics = new GroupChatAnalytics();

        var systemFunctions = serviceProvider.GetRequiredService<SystemAgentFunctions>();
        var webSearchFunctions = serviceProvider.GetRequiredService<WebSearchAgentFunctions>();

        var intentDetectionService = serviceProvider.GetRequiredService<IIntentDetectionService>();

        var coordinator = await CreateAdvancedCoordinatorAsync(apiKey, model, endpoint);
        var systemAgent = await CreateContextAwareSystemAgentAsync(apiKey, model, endpoint, systemFunctions, contextManager);
        var taskAgent = await CreateContextAwareTaskAgentAsync(apiKey, model, endpoint, contextManager, mcpOptions);
        var webResearchAgent = await CreateWebSearchAgentAsync(apiKey, model, endpoint, webSearchFunctions);
        var analyticsAgent = await CreateAnalyticsAgentAsync(apiKey, model, endpoint, analytics);
        var userProxy = CreateEnhancedUserProxy();



        // Optional specialized 
        var agents = new List<IAgent> {systemAgent, taskAgent, webResearchAgent, analyticsAgent };


        // Create intelligent workflow with intent-based routing
        var workflow = CreateIntelligentWorkflow(
           userProxy, coordinator, systemAgent, taskAgent, webResearchAgent, analyticsAgent, options, intentDetectionService); // Intent service geçiliyor


        var groupChat = new SmartGroupChat(
            members: agents,
            workflow: workflow,
            admin: coordinator,
            contextManager: contextManager,
            analytics: analytics,
            options: options);

        await InitializeCollaborativeEnvironmentAsync(groupChat);

        Console.WriteLine($"✅ Intent-Based Group Chat Ready with {agents.Count} agents");
        return groupChat;
    }

    /// <summary>
    /// Advanced Coordinator with context awareness and multi-step planning
    /// </summary>
    private static async Task<IAgent> CreateAdvancedCoordinatorAsync(
        string apiKey, string model, string endpoint)
    {
        var systemMessage = @"Sen yardımcı bir koordinatör asistandsın. Kullanıcı isteklerini uygun uzman asistana yönlendiriyorsun.

UZMAN ASİSTANLAR:
- SystemAgent: Uygulama açma/kapatma, ses kontrolü, cihaz ayarları
- TaskAgent: Görev ekleme, hatırlatıcı kurma, randevu planlama  
- WebSearchAgent: İnternet araması, haber, hava durumu

YÖNLENDİRME:
- Uygulama işlemleri → @SystemAgent
- Görev/hatırlatıcı → @TaskAgent  
- Arama/bilgi → @WebSearchAgent

Örnek: ""Spotify aç"" → ""@SystemAgent Spotify'ı açar mısın?""

Kısa ve net yönlendir, uzun açıklama yapma.";


        return new OpenAIChatAgent(
            chatClient: new ChatClient(model, new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) }),
            name: "Coordinator",
            temperature: 0,
            systemMessage: systemMessage)
            .RegisterMessageConnector()
            .RegisterPrintMessage();
    }

    /// <summary>
    /// Context-aware System Agent with state management
    /// </summary>
    private static async Task<IAgent> CreateContextAwareSystemAgentAsync(
        string apiKey, string model, string endpoint, SystemAgentFunctions functions, ConversationContextManager contextManager)
    {
        var systemMessage = @"Sen sistem kontrolü yapan yardımcı bir asistandsın.

YAPABÍLECEKLERÍN:
✅ Uygulamaları açma/kapatma (Chrome, Spotify, Notepad vb.)
✅ Müzik kontrolü (çal, durdur, sonraki)
✅ Ses seviyesi ayarlama
✅ WiFi/Bluetooth açma/kapatma
✅ Cihaz kontrolleri

YANITLARIN:
- Kısa ve net ol
- Başarılı: ""✅ Spotify açıldı""
- Başarısız: ""❌ Spotify bulunamadı""
- Belirsiz: ""Chrome'u açayım mı?""

Samimi ve yardımcı ol, teknik detay verme.";



        var functionMap = functions.GetFunctionMap();
        return new OpenAIChatAgent(
            chatClient: new ChatClient(model, new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) }),
            name: "SystemAgent",
            systemMessage: systemMessage)
            .RegisterMessageConnector()
            .RegisterMiddleware(new FunctionCallMiddleware(
                functions: [
                    functions.CheckApplicationAsyncFunctionContract,
                    functions.OpenApplicationAsyncFunctionContract,
                    functions.CloseApplicationAsyncFunctionContract,
                    functions.PlayMusicAsyncFunctionContract,
                    functions.ControlDeviceAsyncFunctionContract,
                    functions.IsApplicationRunningAsyncFunctionContract,
                    functions.ControlDeviceAsyncFunctionContract
                ],
                functionMap: functionMap))
            .RegisterPrintMessage();
    }

    /// <summary>
    /// Context-aware Task Agent with intelligent task management
    /// </summary>
    public static async Task<IAgent> CreateContextAwareTaskAgentAsync(
        string apiKey, string model, string endpoint, ConversationContextManager contextManager, McpOptions mcpOptions)
    {


        IMcpClient mcpClient = await McpClient.CreateAsync(
        clientTransport: new HttpClientTransport(new()
        {
            Endpoint = new Uri(mcpOptions.TodoistServerLink),
            Name = "todoist.mcpverse.dev",
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {mcpOptions.TodoistApiKey}"
            }
        }),
        clientOptions: new McpClientOptions
        {
            ClientInfo = new Implementation()
            {
                Name = "MCP.Client",
                Version = "1.0.0"
            }
        });

        var tools = await mcpClient.ListToolsAsync();
        var builder = Kernel.CreateBuilder();

        builder.AddOpenAIChatCompletion(
            modelId: model,
            openAIClient: new OpenAIClient(
                credential: new ApiKeyCredential(apiKey),
                options: new OpenAIClientOptions { Endpoint = new Uri(endpoint) }
            ))
            .Plugins.AddFromFunctions("TodoistAdvanced", tools.Select(x => x.AsKernelFunction()));

        var kernel = builder.Build();
        var systemMessage = @"Sen görev ve hatırlatıcı yönetimi yapan yardımcı bir asistandsın.

YAPABILECEKLERÍN:
✅ Görev ekleme/güncelleme
✅ Hatırlatıcı kurma
✅ Randevu planlama
✅ Görev listesi görüntüleme

YANITLARIN:
- ""✅ Görev eklendi: Alışveriş yap""
- ""⏰ Hatırlatıcı kuruldu: Yarın 14:00""
- ""📋 3 aktif görevin var""

Eksik bilgi varsa sor:
- ""Ne zaman hatırlatayım?""
- ""Görev detayını belirtir misin?""

Samimi ve düzenli ol.";


        return new SemanticKernelAgent(
            kernel,
            name: "TaskAgent",
            systemMessage: systemMessage,
            settings: new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new() { RetainArgumentTypes = true })
            })
            .RegisterMessageConnector()
            .RegisterPrintMessage();
    }

    /// <summary>
    /// Optional Web Search Agent
    /// </summary>
    public static async Task<IAgent> CreateWebSearchAgentAsync(
        string apiKey, string model, string endpoint, WebSearchAgentFunctions functions)
    {
        var systemMessage = @"Sen web araması yapan yardımcı bir asistandsın.

YAPABILECEKLERÍN:
✅ İnternet'te arama yapma
✅ Hava durumu bilgisi
✅ Güncel haberler
✅ Genel bilgi arama

YANITLARIN:
- Sonuçları özetle
- Kaynak belirt
- Kısa ve anlaşılır ol

Örnek:
""🔍 İstanbul hava durumu: 22°C, parçalı bulutlu
Kaynak: weather.com""

Yardımcı ve bilgilendirici ol.";


        var functionMap = functions.GetFunctionMap();

        return new OpenAIChatAgent(
            chatClient: new ChatClient(model, new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) }),
            name: "WebSearchAgent",
            systemMessage: systemMessage)
            .RegisterMessageConnector()
            .RegisterMiddleware(new FunctionCallMiddleware(
                functions: [functions.SearchWebAsyncFunctionContract],
                functionMap: functionMap))
            .RegisterPrintMessage();
    }

    /// <summary>
    /// Analytics Agent for performance monitoring
    /// </summary>
    private static async Task<IAgent> CreateAnalyticsAgentAsync(
        string apiKey, string model, string endpoint, GroupChatAnalytics analytics)
    {
        var systemMessage = @"Sen sistem performansını izleyen yardımcı bir asistandsın.

YAPABILECEKLERÍN:
✅ Kullanım istatistikleri
✅ Performans raporları  
✅ Hata takibi
✅ Kullanıcı tercihlerini öğrenme

YANITLARIN:
- Basit ve anlaşılır
- Sayısal veriler
- Öneriler sun

Teknik olmayan dilde rapor ver.";

        return new OpenAIChatAgent(
            chatClient: new ChatClient(model, new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) }),
            name: "AnalyticsAgent",
            systemMessage: systemMessage)
            .RegisterMessageConnector()
            .RegisterPrintMessage();
    }

    private static IAgent CreateEnhancedUserProxy()
    {
        return new UserProxyAgent(
            name: "User",
            humanInputMode: HumanInputMode.ALWAYS)
            .RegisterPrintMessage();
    }
    private static async Task InitializeCollaborativeEnvironmentAsync(GroupChat groupChat)
    {
        var introMessage = @"🤖 **Smart Voice Assistant - Collaborative Team Ready!**

**Meet Your AI Team:**
• **Coordinator** 🎯 - Routes requests and facilitates collaboration
• **SystemAgent** ⚙️ - Opens apps, controls system, manages devices  
• **TaskAgent** 📝 - Handles tasks, reminders, scheduling
• **WebSearchAgent** 🔍 - Searches web, finds information, research

**How It Works:**
✨ **Natural Conversation**: Just speak naturally - ""Open Spotify and remind me to check new releases""
✨ **Smart Collaboration**: Agents work together automatically
✨ **No Complex Routing**: Direct communication, no API overhead
✨ **Parallel Operations**: Handle multiple requests simultaneously

**Example Commands:**
• ""Open Spotify"" → SystemAgent handles it
• ""Search weather and set reminder"" → WebSearchAgent + TaskAgent collaborate  
• ""Add task to call John tomorrow"" → TaskAgent manages it
• ""What's the news today?"" → WebSearchAgent researches it

**Ready to assist! What would you like to do?** 🚀";

        groupChat.SendAsync([new TextMessage(AutoGen.Core.Role.System, introMessage)]);
    }

    /// <summary>
    /// Creates intelligent workflow with proper agent routing and conversation flow
    /// </summary>
    private static Graph CreateIntelligentWorkflow(
        IAgent userProxy,
        IAgent coordinator,
        IAgent systemAgent,
        IAgent taskAgent,
        IAgent? webAgent,
        IAgent? analyticsAgent,
        GroupChatOptions options,
        IIntentDetectionService intentDetectionService)
    {
        var workflow = new Graph();

        // 1. ENTRY POINT: User always starts with coordinator
        workflow.AddTransition(Transition.Create(userProxy, coordinator));

        // 2. INTENT-BASED ROUTING: Coordinator to specialized agents
        workflow.AddTransition(Transition.Create(coordinator, systemAgent,
            async (from, to, messages) =>
            {
                var shouldRoute = await ShouldRouteToSystemAgent(messages, intentDetectionService);
                return shouldRoute;
            }));


        workflow.AddTransition(Transition.Create(coordinator, taskAgent, canTransitionAsync: async (from, to, messages) =>
        {
            var message = messages.Where(x => x.From == "User").LastOrDefault().GetContent() ?? "";

            var intentResult = await intentDetectionService.DetectIntentAsync(
                message ?? "", "tr");

            var taskCommands = new[]
            {
                CommandType.AddTask,
                CommandType.UpdateTask,
                CommandType.DeleteTask,
                CommandType.SetReminder,
                CommandType.ListTasks
            };
            var shouldRoute = taskCommands.Contains(intentResult.Intent) &&
                              intentResult.Confidence >= 0.3f;

            return shouldRoute;
        }));

        if (options.EnableWebSearchAgent && webAgent != null)
        {
            workflow.AddTransition(Transition.Create(coordinator, webAgent,
                async (from, to, messages) =>
                {
                    var shouldRoute = await ShouldRouteToWebAgent(messages, intentDetectionService);

                    return shouldRoute;
                }));
        }



        // 4. AGENT CHAINING: For multi-step operations
        workflow.AddTransition(Transition.Create(systemAgent, taskAgent,
            async (from, to, messages) =>
            {
                var requiresTask = await RequiresTaskAfterSystem(messages);
                if (requiresTask)
                {
                    Console.WriteLine($"🔗 CHAINING: {from.Name} → {to.Name} (multi-step operation)");
                    var lastMsg = messages.LastOrDefault()?.GetContent() ?? "";
                    Console.WriteLine($"📝 Chain reason: Task action needed after '{lastMsg.Substring(0, Math.Min(50, lastMsg.Length))}...'");
                }
                return requiresTask;
            }));

        if (webAgent != null)
        {
            workflow.AddTransition(Transition.Create(webAgent, taskAgent,
                async (from, to, messages) =>
                {
                    var requiresTask = await RequiresTaskAfterWeb(messages);
                    if (requiresTask)
                    {
                        Console.WriteLine($"🔗 CHAINING: {from.Name} → {to.Name} (multi-step operation)");
                        var lastMsg = messages.LastOrDefault()?.GetContent() ?? "";
                        Console.WriteLine($"📝 Chain reason: Task needed after web search '{lastMsg.Substring(0, Math.Min(50, lastMsg.Length))}...'");
                    }
                    return requiresTask;
                }));
        }


        // 5. ANALYTICS COLLECTION: Optional data gathering
        if (options.EnableAnalyticsAgent && analyticsAgent != null)
        {
            workflow.AddTransition(Transition.Create(systemAgent, analyticsAgent,
                async (from, to, messages) =>
                {
                    var shouldCollect = await ShouldCollectAnalytics(messages);
                    if (shouldCollect)
                    {
                        Console.WriteLine($"📊 ANALYTICS: {from.Name} → {to.Name} (collecting data)");
                    }
                    return shouldCollect;
                }));

            workflow.AddTransition(Transition.Create(taskAgent, analyticsAgent,
                async (from, to, messages) =>
                {
                    var shouldCollect = await ShouldCollectAnalytics(messages);
                    if (shouldCollect)
                    {
                        Console.WriteLine($"📊 ANALYTICS: {from.Name} → {to.Name} (collecting data)");
                    }
                    return shouldCollect;
                }));

            if (webAgent != null)
            {
                workflow.AddTransition(Transition.Create(webAgent, analyticsAgent,
                    async (from, to, messages) =>
                    {
                        var shouldCollect = await ShouldCollectAnalytics(messages);
                        if (shouldCollect)
                        {
                            Console.WriteLine($"📊 ANALYTICS: {from.Name} → {to.Name} (collecting data)");
                        }
                        return shouldCollect;
                    }));
            }

            // Analytics agent also returns to coordinator
            workflow.AddTransition(Transition.Create(analyticsAgent, coordinator,
                async (from, to, messages) =>
                {
                    Console.WriteLine($"🔄 RETURN FLOW: {from.Name} → {to.Name} (analytics completed)");
                    return true;
                }));

            Console.WriteLine("✅ Added: Analytics collection workflow");
        }

        // 6. CONVERSATION TERMINATION: Coordinator back to user
        workflow.AddTransition(Transition.Create(coordinator, userProxy,
            async (from, to, messages) =>
            {
                // Check if coordinator has a final response to give
                var lastMessage = messages.LastOrDefault();
                var shouldTerminate = lastMessage?.From == "AdvancedCoordinator" ||
                                     lastMessage?.From == "Coordinator";

                if (shouldTerminate)
                {
                    Console.WriteLine($"🏁 CONVERSATION END: {from.Name} → {to.Name} (final response)");
                }

                return shouldTerminate;
            }));

        Console.WriteLine("✅ Added: Coordinator → User (conversation termination)");



        return workflow;
    }

    /// <summary>
    /// Debug helper for transition validation
    /// </summary>
    public static void ValidateWorkflowTransitions(Graph workflow)
    {
        Console.WriteLine("🔍 WORKFLOW TRANSITION VALIDATION");
        Console.WriteLine("═══════════════════════════════════");

        // This would require accessing internal Graph structure
        // Implementation depends on AutoGen's Graph internal API

        Console.WriteLine("✅ Workflow structure appears valid");
        Console.WriteLine();
    }

    /// <summary>
    /// Intent-based routing methods using IntentDetectionService
    /// </summary>
    private static async Task<bool> ShouldRouteToSystemAgent(
    IEnumerable<IMessage> context,
    IIntentDetectionService intentDetectionService)
    {
        var message = context.Where(x => x.From == "User").LastOrDefault().GetContent() ?? "";
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[RoutingTest:SystemAgent] Incoming message: '{message}'");
        Console.ResetColor();

        if (string.IsNullOrEmpty(message)) return false;

        try
        {
            var intentResult = await intentDetectionService.DetectIntentAsync(message, "tr");

            Console.WriteLine($"[RoutingTest:SystemAgent] Detected intent: {intentResult.Intent}, " +
                              $"Confidence: {intentResult.Confidence:F2}");

            var systemCommands = new[]
            {
            CommandType.OpenApplication,
            CommandType.CloseApplication,
            CommandType.PlayMusic,
            CommandType.ControlDevice,
        };

            var shouldRoute = systemCommands.Contains(intentResult.Intent) &&
                              intentResult.Confidence >= 0.3f;

            Console.WriteLine($"[RoutingTest:SystemAgent] Should route? {shouldRoute}");

            return shouldRoute;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Intent detection failed: {ex.Message}");
            var keywordFallback = await ContainsSystemKeywords(context);
            Console.WriteLine($"[RoutingTest:SystemAgent] Keyword fallback result: {keywordFallback}");
            return keywordFallback;
        }
    }


    private static async Task<bool> ShouldRouteToWebAgent(
        IEnumerable<IMessage> context,
        IIntentDetectionService intentDetectionService)
    {
        var message = context.LastOrDefault()?.GetContent() ?? "";
        if (string.IsNullOrEmpty(message)) return false;

        try
        {
            var intentResult = await intentDetectionService.DetectIntentAsync(message, "tr");

            // Route to WebAgent based on SearchWeb intent
            var shouldRoute = intentResult.Intent == CommandType.SearchWeb &&
                             intentResult.Confidence >= 0.3f;

            return shouldRoute;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Intent detection failed for web routing: {ex.Message}");
            return await ContainsWebKeywords(context);
        }
    }

    /// <summary>
    /// Advanced chaining logic based on message content and intent
    /// </summary>
    private static async Task<bool> RequiresTaskAfterSystem(IEnumerable<IMessage> context)
    {
        var message = context.LastOrDefault()?.GetContent()?.ToLower() ?? "";

        // Check for sequential action indicators
        var sequentialIndicators = new[] {
        "sonra", "then", "hatırlat", "kaydet", "not al",
        "randevu kur", "toplantı ekle", "görev oluştur"
    };

        return sequentialIndicators.Any(indicator => message.Contains(indicator));
    }

    private static async Task<bool> RequiresTaskAfterWeb(IEnumerable<IMessage> context)
    {
        var message = context.LastOrDefault()?.GetContent()?.ToLower() ?? "";

        // Web search results that should be saved or scheduled
        var taskIndicators = new[] {
        "hatırlat", "kaydet", "görev ekle", "randevu",
        "not al", "daha sonra", "takip et"
    };

        return taskIndicators.Any(indicator => message.Contains(indicator));
    }

    private static async Task<bool> ShouldCollectAnalytics(IEnumerable<IMessage> context)
    {
        // Collect analytics for every 5th interaction or on errors
        var messageCount = context.Count();
        var lastMessage = context.LastOrDefault()?.GetContent()?.ToLower() ?? "";

        var hasError = lastMessage.Contains("hata") || lastMessage.Contains("error") ||
                       lastMessage.Contains("başarısız") || lastMessage.Contains("failed");

        return messageCount % 5 == 0 || hasError;
    }

    /// <summary>
    /// Fallback keyword-based routing methods (kept for compatibility)
    /// </summary>
    private static async Task<bool> ContainsSystemKeywords(IEnumerable<IMessage> context)
    {
        var message = context.LastOrDefault()?.GetContent()?.ToLower() ?? "";
        var keywords = new[] { "aç", "open", "kapat", "close", "çal", "play", "durdur", "ses", "volume", "bluetooth", "wifi" };
        return keywords.Any(k => message.Contains(k));
    }

    private static async Task<bool> ContainsWebKeywords(IEnumerable<IMessage> context)
    {
        var message = context.LastOrDefault()?.GetContent()?.ToLower() ?? "";
        var keywords = new[] { "ara", "search", "haber", "news", "hava", "weather", "google", "web" };
        return keywords.Any(k => message.Contains(k));
    }

}