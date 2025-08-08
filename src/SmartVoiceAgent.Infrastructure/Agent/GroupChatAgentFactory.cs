using AutoGen;
using AutoGen.Core;
using AutoGen.OpenAI;
using AutoGen.OpenAI.Extension;
using AutoGen.SemanticKernel;
using AutoGen.SemanticKernel.Extension;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI;
using OpenAI.Chat;
using SmartVoiceAgent.Application.Agent;
using SmartVoiceAgent.Core.Models;
using SmartVoiceAgent.Infrastructure.Agent;
using SmartVoiceAgent.Infrastructure.Mcp;
using SmartVoiceAgent.Infrastructure.Middlewares;
using System.ClientModel;
using System.Text.Json;

/// <summary>
/// Advanced Group Chat System with Context Management, Memory, and Analytics
/// </summary>
public static class GroupChatAgentFactory
{
    /// <summary>
    /// Creates a production-ready group chat system with context awareness
    /// </summary>
    public static async Task<SmartGroupChat> CreateGroupChatAsync(
        string apiKey,
        string model,
        string endpoint,
        Functions functions,
        IConfiguration configuration,
        GroupChatOptions options = null)
    {
        options ??= new GroupChatOptions();
        var mcpOptions = new McpOptions();
        configuration.GetSection("Mcpverse").Bind(mcpOptions);
        Console.WriteLine("🏗️ Building Advanced Group Chat System...");

        // Create context-aware agents
        var contextManager = new ConversationContextManager();
        var analytics = new GroupChatAnalytics();

        var coordinator = await CreateAdvancedCoordinatorAsync(apiKey, model, endpoint, contextManager);
        var systemAgent = await CreateContextAwareSystemAgentAsync(apiKey, model, endpoint, functions, contextManager);
        var taskAgent = await CreateContextAwareTaskAgentAsync(apiKey, model, endpoint, contextManager, mcpOptions);
        var webResearchAgent = await CreateWebSearchAgentAsync(apiKey, model, endpoint, functions);
        var analyticsAgent = await CreateAnalyticsAgentAsync(apiKey, model, endpoint, analytics);
        var userProxy = CreateEnhancedUserProxy();

        // Optional specialized agents
        var agents = new List<IAgent> { coordinator, systemAgent, taskAgent, webResearchAgent, userProxy, analyticsAgent };



        // Create smart group chat with enhanced workflow
        var workflow = CreateIntelligentWorkflow(userProxy, coordinator, systemAgent, taskAgent, webResearchAgent, analyticsAgent, options);

        var groupChat = new SmartGroupChat(
            members: agents,
            workflow: workflow,
            admin: coordinator,
            contextManager: contextManager,
            analytics: analytics,
            options: options);

        Console.WriteLine($"✅ Group Chat Ready with {agents.Count} agents");
        return groupChat;
    }

    /// <summary>
    /// Advanced Coordinator with context awareness and multi-step planning
    /// </summary>
    private static async Task<IAgent> CreateAdvancedCoordinatorAsync(
        string apiKey, string model, string endpoint, ConversationContextManager contextManager)
    {
        var systemMessage = @"Sen gelişmiş bir AI koordinatörü olarak grup sohbetini yönetiyorsun.

=== ADVANCED CAPABILITIES ===
1. **Context Awareness**: Önceki konuşmaları hatırla ve bağlamı koru
2. **Multi-Step Planning**: Karmaşık görevleri adımlara böl
3. **Parallel Execution**: Birden fazla agent'ı aynı anda çalıştır
4. **Error Recovery**: Hatalarda alternatif çözümler üret
5. **User Experience**: Kullanıcıya süreç hakkında bilgi ver

=== GELIŞMIŞ ROUTING LOGIC ===
**Immediate Actions (Paralel çalıştır):**
- ""Chrome aç ve müzik çal"" → @SystemAgent (2 task parallel)
- ""Görev ekle ve hatırlatma kur"" → @TaskAgent (2 MCP call parallel)

**Sequential Actions (Sıralı çalıştır):**
- ""Hava durumunu ara sonra hatırlat"" → @WebAgent → @TaskAgent
- ""Dosya aç sonra email gönder"" → @SystemAgent → @TaskAgent

**Complex Planning:**
User: ""Yarın için tam günlük plan hazırla""
Sen: ""Şunları yapacağım:
1. @TaskAgent Mevcut görevleri listele
2. @WebAgent Hava durumu kontrol et  
3. @TaskAgent Optimized schedule oluştur
4. @TaskAgent Hatırlatmaları kur""

=== CONTEXT MANAGEMENT ===
- Her konuşmada önceki bağlamı referans al
- Kullanıcı tercihlerini hatırla (ses seviyesi, sık kullanılan uygulamalar)
- Incomplete görevleri takip et

=== USER COMMUNICATION ===
- İşlem başlarken: ""3 adımlık planı başlatıyorum...""
- İşlem sırasında: ""Adım 1/3 tamamlandı, Adım 2 başlıyor...""
- Başarı: ""✅ Tüm işlemler başarıyla tamamlandı!""
- Hata: ""❌ X hatası oluştu, alternatif Y'yi deniyorum...""

Sen sadece bir router değil, akıllı bir asistan yöneticisisin!";

        return new OpenAIChatAgent(
            chatClient: new ChatClient(model, new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) }),
            name: "AdvancedCoordinator",
            systemMessage: systemMessage)
            .RegisterMessageConnector()
            .RegisterMiddleware(new ContextAwareMiddleware(contextManager))
            .RegisterPrintMessage();
    }

    /// <summary>
    /// Context-aware System Agent with state management
    /// </summary>
    private static async Task<IAgent> CreateContextAwareSystemAgentAsync(
        string apiKey, string model, string endpoint, Functions functions, ConversationContextManager contextManager)
    {
        var systemMessage = @"Sen akıllı bir sistem kontrolcüsü olarak çalışıyorsun.

=== CONTEXT AWARENESS ===
- Açık uygulamaları takip et (""Spotify zaten açık"")
- Kullanıcı tercihlerini hatırla (""Ses seviyesi %70'e ayarlandı"")
- Son işlemleri referans al (""Chrome'u 5 dakika önce açmıştın"")

=== SMART BEHAVIOR ===
- Duplicate işlemleri engelle
- Optimize suggestions: ""Spotify yerine YouTube Music öneriyorum""  
- State transitions: ""Chrome → Private Mode → Specific URL""

=== PROACTIVE ACTIONS ===
- ""Müzik çal"" → Spotify açık değilse önce aç
- ""Chrome kapat"" → Kayıtlı sekmeleri sor
- ""Ses artır"" → Mevcut seviyeyi söyle

=== ERROR HANDLING ===
- App bulunamazsa alternatif öner
- Permission hatalarında çözüm yolu göster
- Hardware problems için diagnostic başlat

Sadece komutları çalıştırma, akıllı sistem yönetimi yap!";

        var functionMap = await CreateAdvancedSystemFunctionMap(functions, contextManager);

        return new OpenAIChatAgent(
            chatClient: new ChatClient(model, new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) }),
            name: "ContextAwareSystemAgent",
            systemMessage: systemMessage)
            .RegisterMessageConnector()
            .RegisterMiddleware(new FunctionCallMiddleware(
                functions: [
                    functions.ProcessVoiceCommandAsyncFunctionContract,
                    functions.OpenApplicationAsyncFunctionContract,
                    functions.CloseApplicationAsyncFunctionContract,
                    functions.PlayMusicAsyncFunctionContract,
                    functions.ControlDeviceAsyncFunctionContract,
                    functions.SearchWebAsyncFunctionContract
                ],
                functionMap: functionMap))
            .RegisterMiddleware(new ContextAwareMiddleware(contextManager))
            .RegisterPrintMessage();
    }

    /// <summary>
    /// Context-aware Task Agent with intelligent task management
    /// </summary>
    private static async Task<IAgent> CreateContextAwareTaskAgentAsync(
        string apiKey, string model, string endpoint, ConversationContextManager contextManager, McpOptions mcpOptions)
    {


        IMcpClient mcpClient = await McpClientFactory.CreateAsync(
        clientTransport: new SseClientTransport(new()
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

        return new SemanticKernelAgent(
            kernel,
            name: "ContextAwareTaskAgent",
            systemMessage: @"Sen gelişmiş görev yönetimi uzmanısın.

=== CONTEXT-DRIVEN TASK MANAGEMENT ===
- Recurring patterns: ""Her Pazartesi toplantı"" → Otomatik recurring task
- Related tasks: ""Doktor randevusu"" → Insurance, documents, reminder chain
- Time intelligence: ""Yarın"" → Exact date calculation
- Priority inference: ""Acil"" vs ""Yapılabilir"" 

=== INTELLIGENT SCHEDULING ===
- Conflict detection: Çakışan randevuları yakala
- Buffer time: Toplantı aralarında 15dk buffer
- Location awareness: Trafik hesabı ile departure reminder
- Dependency chains: Task A → Task B → Task C

=== PROACTIVE FEATURES ===
- Deadline warnings: ""3 gün kaldı"" 
- Completion suggestions: ""İlgili görevler: X, Y, Z""
- Context suggestions: ""Toplantı için hazırlık gerekiyor mu?""

=== NATURAL LANGUAGE PROCESSING ===
- ""Gelecek hafta bir gün doktor"" → Flexible scheduling
- ""Bu işi bitirince hatırlat"" → Conditional reminder
- ""Önemli toplantıdan önce"" → Smart timing

Sen sadece görev eklemiyorsun, akıllı yaşam asistanı oluyorsun!",
            settings: new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new() { RetainArgumentTypes = true })
            })
            .RegisterMessageConnector()
            .RegisterMiddleware(new ContextAwareMiddleware(contextManager))
            .RegisterPrintMessage();
    }

    /// <summary>
    /// Optional Web Search Agent
    /// </summary>
    private static async Task<IAgent> CreateWebSearchAgentAsync(
        string apiKey, string model, string endpoint, Functions functions)
    {
        var systemMessage = @"Sen web araştırma uzmanısın.

=== SEARCH EXPERTISE ===
- Query optimization: Kısa input → Effective search terms
- Source evaluation: Güvenilir kaynakları öncelikle
- Information synthesis: Birden fazla kaynaktan özet
- Follow-up suggestions: İlgili aramalar öner

=== SMART SEARCH ===
- ""Hava durumu"" → Location-based search
- ""Haberler"" → Recent + relevant news
- ""Tarif"" → Recipe with ingredients available

Hızlı ve doğru bilgi getir!";

        var functionMap = new Dictionary<string, Func<string, Task<string>>>
        {
            ["SearchWebAsync"] = async (args) =>
            {
                try
                {
                    var jsonArgs = JsonSerializer.Deserialize<Dictionary<string, object>>(args);
                    var query = jsonArgs["query"]?.ToString() ?? "";
                    var lang = jsonArgs.ContainsKey("lang") ? jsonArgs["lang"]?.ToString() : "tr";
                    var results = jsonArgs.ContainsKey("results") ? Convert.ToInt32(jsonArgs["results"]) : 5;

                    var result = await functions.SearchWebAsync(query, lang, results);
                    return ParseJsonResponse(result, $"🔍 '{query}' araması tamamlandı");
                }
                catch (Exception ex)
                {
                    return $"❌ Arama hatası: {ex.Message}";
                }
            }
        };

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
        var systemMessage = @"Sen sistem analitik uzmanısın.

=== ANALYTICS CAPABILITIES ===
- Performance monitoring: Response times, success rates
- Usage patterns: Sık kullanılan komutlar, peak hours
- Error analysis: Common failures, improvement suggestions
- User behavior: Preferences, workflow optimization

Sadece rapor et, proaktif öneriler sun!";

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
            humanInputMode: HumanInputMode.NEVER)
            .RegisterPrintMessage();
    }

    /// <summary>
    /// Creates intelligent workflow with parallel and conditional execution
    /// </summary>
    private static Graph CreateIntelligentWorkflow(
        IAgent userProxy,
        IAgent coordinator,
        IAgent systemAgent,
        IAgent taskAgent,
        IAgent? webAgent,
        IAgent? analyticsAgent,
        GroupChatOptions options)
    {
        var workflow = new Graph();

        workflow.AddTransition(Transition.Create(userProxy, coordinator));
        workflow.AddTransition(Transition.Create(coordinator, systemAgent, async (_, _, ctx) => await ContainsSystemKeywords(ctx)));
        workflow.AddTransition(Transition.Create(coordinator, taskAgent, async (_, _, ctx) => await ContainsTaskKeywords(ctx)));

        if (options.EnableWebSearchAgent && webAgent != null)
        {
            workflow.AddTransition(Transition.Create(coordinator, webAgent, async (_, _, ctx) => await ContainsWebKeywords(ctx)));
            workflow.AddTransition(Transition.Create(webAgent, taskAgent, async (_, _, ctx) => await RequiresTaskAfterWeb(ctx)));
            workflow.AddTransition(Transition.Create(webAgent, coordinator));
        }

        workflow.AddTransition(Transition.Create(systemAgent, taskAgent, async (_, _, ctx) => await RequiresTaskAfterSystem(ctx)));

        if (options.EnableAnalyticsAgent && analyticsAgent != null)
        {
            workflow.AddTransition(Transition.Create(systemAgent, analyticsAgent));
            workflow.AddTransition(Transition.Create(taskAgent, analyticsAgent));
            if (webAgent != null)
                workflow.AddTransition(Transition.Create(webAgent, analyticsAgent));
        }

        workflow.AddTransition(Transition.Create(systemAgent, coordinator));
        workflow.AddTransition(Transition.Create(taskAgent, coordinator));
        if (analyticsAgent != null)
            workflow.AddTransition(Transition.Create(analyticsAgent, coordinator));

        workflow.AddTransition(Transition.Create(coordinator, userProxy));

        return workflow;
    }

    // Workflow condition methods
    private static async Task<bool> ContainsSystemKeywords(IEnumerable<IMessage> context)
    {
        var message = context.LastOrDefault()?.GetContent()?.ToLower() ?? "";
        var keywords = new[] { "aç", "open", "kapat", "close", "çal", "play", "durdur", "ses", "volume", "bluetooth", "wifi" };
        return keywords.Any(k => message.Contains(k));
    }

    private static async Task<bool> ContainsTaskKeywords(IEnumerable<IMessage> context)
    {
        var message = context.LastOrDefault()?.GetContent()?.ToLower() ?? "";
        var keywords = new[] { "görev", "task", "hatırla", "remind", "todo", "randevu", "appointment", "toplantı", "meeting" };
        return keywords.Any(k => message.Contains(k));
    }

    private static async Task<bool> ContainsWebKeywords(IEnumerable<IMessage> context)
    {
        var message = context.LastOrDefault()?.GetContent()?.ToLower() ?? "";
        var keywords = new[] { "ara", "search", "haber", "news", "hava", "weather", "google", "web" };
        return keywords.Any(k => message.Contains(k));
    }

    private static async Task<bool> RequiresTaskAfterSystem(IEnumerable<IMessage> context)
    {
        var message = context.LastOrDefault()?.GetContent()?.ToLower() ?? "";
        return message.Contains("sonra") || message.Contains("then") || message.Contains("hatırlat");
    }

    private static async Task<bool> RequiresTaskAfterWeb(IEnumerable<IMessage> context)
    {
        var message = context.LastOrDefault()?.GetContent()?.ToLower() ?? "";
        return message.Contains("hatırlat") || message.Contains("kaydet") || message.Contains("görev");
    }

    /// <summary>
    /// Advanced function map with context awareness
    /// </summary>
    private static async Task<Dictionary<string, Func<string, Task<string>>>> CreateAdvancedSystemFunctionMap(
        Functions functions, ConversationContextManager contextManager)
    {
        return new Dictionary<string, Func<string, Task<string>>>
        {
            ["ProcessVoiceCommandAsync"] = async (args) =>
            {
                try
                {
                    var jsonArgs = JsonSerializer.Deserialize<Dictionary<string, object>>(args);
                    var userInput = jsonArgs["userInput"]?.ToString() ?? "";
                    var language = jsonArgs.ContainsKey("language") ? jsonArgs["language"]?.ToString() : "tr";

                    // Add context to command processing
                    var context = contextManager.GetRelevantContext(userInput);

                    var result = await functions.ProcessVoiceCommandAsync(userInput, language, context);

                    // Update context with result
                    contextManager.UpdateContext("system_command", userInput, result);

                    return ParseJsonResponse(result);
                }
                catch (Exception ex)
                {
                    return $"❌ Hata: {ex.Message}";
                }
            },

            ["OpenApplicationAsync"] = async (args) =>
            {
                try
                {
                    var jsonArgs = JsonSerializer.Deserialize<Dictionary<string, object>>(args);
                    var appName = jsonArgs["applicationName"]?.ToString() ?? "";

                    // Check if app is already open
                    if (contextManager.IsApplicationOpen(appName))
                    {
                        return $"ℹ️ {appName} zaten açık";
                    }

                    var result = await functions.OpenApplicationAsync(appName);

                    // Update application state
                    contextManager.SetApplicationState(appName, true);

                    return ParseJsonResponse(result, $"✅ {appName} açıldı");
                }
                catch (Exception ex)
                {
                    return $"❌ Uygulama açma hatası: {ex.Message}";
                }
            },

            // ... other functions with context awareness
        };
    }

    private static string ParseJsonResponse(string jsonResult, string defaultMessage = "İşlem tamamlandı")
    {
        try
        {
            var jsonDocument = JsonDocument.Parse(jsonResult);
            var root = jsonDocument.RootElement;

            if (root.TryGetProperty("success", out var successElement) && successElement.GetBoolean())
            {
                if (root.TryGetProperty("message", out var messageElement))
                {
                    return messageElement.GetString() ?? defaultMessage;
                }
                return defaultMessage;
            }
            else if (root.TryGetProperty("error", out var errorElement))
            {
                return $"❌ {errorElement.GetString()}";
            }
        }
        catch
        {
            // JSON parse edilemezse default döndür
        }
        return defaultMessage;
    }
}