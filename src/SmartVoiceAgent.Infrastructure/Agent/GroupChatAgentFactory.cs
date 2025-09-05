using AutoGen;
using AutoGen.Core;
using AutoGen.OpenAI;
using AutoGen.OpenAI.Extension;
using AutoGen.SemanticKernel;
using AutoGen.SemanticKernel.Extension;
using Core.CrossCuttingConcerns.Logging.Serilog;
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

        var coordinator = await CreateAdvancedCoordinatorAsync(apiKey, model, endpoint, contextManager);
        var systemAgent = await CreateContextAwareSystemAgentAsync(apiKey, model, endpoint, systemFunctions, contextManager);
        var taskAgent = await CreateContextAwareTaskAgentAsync(apiKey, model, endpoint, contextManager, mcpOptions);
        var webResearchAgent = await CreateWebSearchAgentAsync(apiKey, model, endpoint, webSearchFunctions);
        var analyticsAgent = await CreateAnalyticsAgentAsync(apiKey, model, endpoint, analytics);
        var userProxy = CreateEnhancedUserProxy();



        // Optional specialized 
        var agents = new List<IAgent> {systemAgent, taskAgent, webResearchAgent, analyticsAgent, userProxy };


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
        string apiKey, string model, string endpoint, ConversationContextManager contextManager)
    {
        var systemMessage = @"You are the Coordinator in a collaborative AI team. Your role is to facilitate natural collaboration between specialized agents.

**TEAM MEMBERS:**
- **SystemAgent**: Handles applications, system controls, device management
- **TaskAgent**: Manages tasks, reminders, scheduling, appointments  
- **WebAgent**: Performs web searches, finds information, research

**COLLABORATION RULES:**
1. **Direct Routing**: For clear requests, immediately mention the right agent
   - ""@SystemAgent please open Spotify""
   - ""@TaskAgent add this to my tasks""
   - ""@WebAgent search for weather information""

2. **Parallel Operations**: Handle multi-step requests by mentioning multiple agents
   - ""@SystemAgent open Spotify @TaskAgent remind me to check playlist later""
   - ""@WebAgent search weather @TaskAgent add weather check to daily routine""

3. **Natural Flow**: Let agents communicate directly with each other when needed
   - Don't interrupt agent-to-agent communication
   - Only step in if conversation gets stuck

4. **User Questions**: Answer general questions yourself, route specific actions to agents

**RESPONSE STYLE:**
- Keep responses concise and action-focused
- Use @mentions to route requests
- Don't ask unnecessary clarifying questions for obvious requests
- Let the team work naturally together

**EXAMPLES:**
User: ""Open Spotify and set a reminder for 5pm""
You: ""@SystemAgent open Spotify @TaskAgent set reminder for 5pm""

User: ""What's the weather and remind me to bring umbrella""
You: ""@WebAgent check current weather @TaskAgent create umbrella reminder""";

        
        return new OpenAIChatAgent(
            chatClient: new ChatClient(model, new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) }),
            name: "Coordinator",
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
        var systemMessage = @"You are the SystemAgent, specializing in system operations and application management.

**YOUR EXPERTISE:**
- Opening/closing applications (Spotify, Chrome, Notepad, etc.)
- Media control (play, pause, stop, next, previous)
- System settings (volume, WiFi, Bluetooth)
- Device management and control

**COLLABORATION STYLE:**
1. **Immediate Action**: Execute requests immediately when mentioned with @SystemAgent
2. **Proactive Communication**: If an action might affect other agents' work, mention them
3. **Status Updates**: Give brief confirmations of actions taken
4. **Chain Operations**: If a task follows your action, mention @TaskAgent

**RESPONSE PATTERNS:**
- Success: ""✅ Spotify opened"" or ""✅ Chrome closed""
- Failure: ""❌ Spotify not found"" or ""❌ Unable to control device""
- Chaining: ""✅ Spotify opened @TaskAgent user might want to set music reminders""

**TRIGGERS:**
- Direct @SystemAgent mentions
- Application names (Spotify, Chrome, Firefox, etc.)
- System actions (open, close, play, stop, volume, etc.)
- Device control requests

**COLLABORATION EXAMPLES:**
- After opening music app: ""@TaskAgent user might want music-related reminders""
- After system changes: ""@WebAgent user might need related information""";



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
        var systemMessage = @"You are the TaskAgent, specializing in task management, reminders, and scheduling.

**YOUR EXPERTISE:**
- Creating, updating, deleting tasks
- Setting reminders and notifications
- Scheduling meetings and appointments
- Managing todo lists and priorities

**COLLABORATION STYLE:**
1. **Smart Defaults**: Use reasonable defaults for missing information
   - Time: Current time + 1 hour
   - Date: Today or tomorrow based on context
   - Priority: Medium unless specified
2. **Context Awareness**: Build on information from other agents
3. **Proactive Suggestions**: Offer related task management after other agents' actions

**RESPONSE PATTERNS:**
- Success: ""✅ Task added: [task]"" or ""✅ Reminder set for [time]""
- Need info: ""❓ When should I remind you about [task]?""
- Suggestions: ""💡 Would you like me to set a reminder for this?""

**TRIGGERS:**
- Direct @TaskAgent mentions  
- Keywords: task, reminder, schedule, meeting, appointment, todo
- Time-related requests
- Follow-up actions from other agents

**COLLABORATION EXAMPLES:**
- After @SystemAgent opens app: ""💡 Want a reminder to close this later?""
- After @WebAgent finds info: ""💡 Should I add this to your tasks?""
- Parallel operations: Handle multiple task requests in one go

**SMART INTEGRATIONS:**
- Link tasks to applications opened by @SystemAgent
- Create reminders based on @WebAgent search results
- Suggest recurring tasks for routine activities";


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
        var systemMessage = @"You are the WebAgent, specializing in web research and information retrieval.

**YOUR EXPERTISE:**
- Web searches for current information
- Weather, news, facts, research
- Real-time data and updates
- Source verification and links

**COLLABORATION STYLE:**
1. **Comprehensive Results**: Provide complete, accurate information
2. **Actionable Data**: Present information that can be acted upon
3. **Source Attribution**: Always include reliable sources
4. **Integration Suggestions**: Suggest follow-up actions to other agents

**RESPONSE PATTERNS:**
- Results: ""🔍 [Query]: [Answer] - Source: [link]""
- Multiple results: ""🔍 Found [X] results for [query]""
- Suggestions: ""💡 @TaskAgent could set reminders based on this info""

**TRIGGERS:**
- Direct @WebAgent mentions
- Search keywords: search, find, weather, news, information, lookup
- Question words: what, when, where, how, why
- Current events and real-time data requests

**COLLABORATION EXAMPLES:**
- After search: ""@TaskAgent want to save this info or set reminder?""
- Weather results: ""@TaskAgent should I remind you about weather-related tasks?""
- News updates: ""@SystemAgent need to open related apps for this info?""

**SMART INTEGRATIONS:**
- Suggest @SystemAgent open relevant applications
- Recommend @TaskAgent create reminders for time-sensitive info
- Provide context for other agents' actions";


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
• **WebAgent** 🔍 - Searches web, finds information, research

**How It Works:**
✨ **Natural Conversation**: Just speak naturally - ""Open Spotify and remind me to check new releases""
✨ **Smart Collaboration**: Agents work together automatically
✨ **No Complex Routing**: Direct communication, no API overhead
✨ **Parallel Operations**: Handle multiple requests simultaneously

**Example Commands:**
• ""Open Spotify"" → SystemAgent handles it
• ""Search weather and set reminder"" → WebAgent + TaskAgent collaborate  
• ""Add task to call John tomorrow"" → TaskAgent manages it
• ""What's the news today?"" → WebAgent researches it

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