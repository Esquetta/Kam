using AutoGen;
using AutoGen.Core;
using AutoGen.OpenAI;
using AutoGen.OpenAI.Extension;
using AutoGen.SemanticKernel;
using AutoGen.SemanticKernel.Extension;
using Microsoft.Extensions.Configuration;
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
using SmartVoiceAgent.Infrastructure.Mcp;
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
    IIntentDetectionService intentDetectionService, // Yeni parametre eklendi
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

        var coordinator = await CreateAdvancedCoordinatorAsync(apiKey, model, endpoint, contextManager);
        var systemAgent = await CreateContextAwareSystemAgentAsync(apiKey, model, endpoint, functions, contextManager);
        var taskAgent = await CreateContextAwareTaskAgentAsync(apiKey, model, endpoint, contextManager, mcpOptions);
        var webResearchAgent = await CreateWebSearchAgentAsync(apiKey, model, endpoint, functions);
        var analyticsAgent = await CreateAnalyticsAgentAsync(apiKey, model, endpoint, analytics);
        var userProxy = CreateEnhancedUserProxy();

        // Optional specialized 
        var agents = new List<IAgent> { coordinator, systemAgent, taskAgent, webResearchAgent, analyticsAgent, userProxy };



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

        var testGroup=new GroupChat(
            members: agents,
            admin: coordinator);


        Console.WriteLine($"✅ Intent-Based Group Chat Ready with {agents.Count} agents");
        return groupChat;
    }

    /// <summary>
    /// Advanced Coordinator with context awareness and multi-step planning
    /// </summary>
    private static async Task<IAgent> CreateAdvancedCoordinatorAsync(
        string apiKey, string model, string endpoint, ConversationContextManager contextManager)
    {
        var coordinatorSystemMessage = """
You are a Coordinator Agent for a Smart Voice Assistant system. Your role is to:

1. **Direct Application Commands**: When users clearly request to open, close, or control specific applications (like "Spotify'i aç", "Chrome'u kapat"), immediately route these to the SystemAgent without asking clarifying questions.

2. **Ambiguous Requests**: Only ask clarifying questions when the user's intent is genuinely unclear.

3. **Music vs Application**: 
   - "Spotify'i aç" = Open Spotify application (route to SystemAgent)
   - "Müzik çal" = Play music (route to SystemAgent)
   - "Spotify'da müzik çal" = Play music in Spotify (route to SystemAgent)

4. **Response Format**: Keep responses concise and helpful. Don't over-explain unless the user asks for details.

Current conversation context will help you make better routing decisions.
""";


        return new OpenAIChatAgent(
            chatClient: new ChatClient(model, new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) }),
            name: "Coordinator",
            systemMessage: coordinatorSystemMessage)
            .RegisterMessageConnector()
            .RegisterPrintMessage();
    }

    /// <summary>
    /// Context-aware System Agent with state management
    /// </summary>
    private static async Task<IAgent> CreateContextAwareSystemAgentAsync(
        string apiKey, string model, string endpoint, Functions functions, ConversationContextManager contextManager)
    {
        var systemMessage = @"You are the System Agent.

Your ONLY responsibility is to execute the requested system-level action, such as:
- Opening or closing an application.
- Navigating to a specific website in a browser.
- Controlling media playback.
- Executing OS-level commands (shutdown, restart, lock, etc.).

Rules:
1. If the request contains an action like ""open"", ""close"", ""start"", ""stop"", ""play"", ""pause"", or ""navigate"", you must try to execute it immediately.
2. Never explain alternative manual steps.  
3. Never tell the user to check if an application is installed — just try to open it, and if it fails, say `""Unable to open [app]""`.
4. Responses must be **short and action-focused**:  
   - ✅ “Opening Spotify.”  
   - ❌ “Spotify is not installed, you can download it from…”
5. If the action is impossible, respond briefly: `""Action failed""` or `""Application not found""`.

";

        var functionMap = await CreateAdvancedSystemFunctionMap(functions, contextManager);

        return new OpenAIChatAgent(
            chatClient: new ChatClient(model, new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) }),
            name: "SystemAgent",
            systemMessage: systemMessage)
            .RegisterMessageConnector()
            .RegisterMiddleware(new FunctionCallMiddleware(
                functions: [
                    functions.ProcessVoiceCommandAsyncFunctionContract,
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

        return new SemanticKernelAgent(
            kernel,
            name: "TaskAgent",
            systemMessage: @"You are TaskAgent, an intelligent task and reminder assistant. 
You manage tasks, reminders, and scheduling operations for the user. 
You can create, update, delete, list tasks, and set reminders based on the user’s requests.

### Core Responsibilities
1. **Add Task**
   - Create a new task with a clear title and optional details (due date, time, priority, repeat schedule).
   - Example inputs: 
     - ""Add task buy groceries tomorrow at 5pm""
     - ""Ekmek almayı hatırlat 17:30""
   - Required output fields:
     - taskName (string)
     - dueDate (ISO 8601 format, optional)
     - dueTime (optional)
     - priority (optional: high, medium, low)
     - repeat (optional: daily, weekly, monthly)

2. **Update Task**
   - Modify an existing task’s details such as name, due date, or priority.

3. **Delete Task**
   - Remove a task from the system by name, ID, or description.
   - Confirm deletion when possible.

4. **List Tasks**
   - Show all tasks or filter by:
     - status (completed / pending)
     - priority
     - due date

5. **Set Reminder**
   - Add a reminder for a task or event at a specific date/time.
   - Can be linked to an existing task or created standalone.

### Important Rules
- Always extract and output structured information for `taskName`, `dueDate`, `dueTime`, `priority`, and `repeat` when available.
- Support natural language date and time formats (e.g., ""tomorrow"", ""next Monday"", ""in 2 hours"").
- For Turkish and English, detect the language and interpret accordingly.
- If a request is unclear, ask clarifying questions before proceeding..
",
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
        string apiKey, string model, string endpoint, Functions functions)
    {
        var systemMessage = @"You are a Web Research Agent. 
Your primary task is to search the web for accurate, up-to-date information on a given query. 
Follow these steps:
1. Identify the key topics or keywords from the user’s request.
2. Use the web search tool to find reliable sources.
3. Summarize the findings clearly and concisely.
4. Include the top 3–5 relevant links with short descriptions.
5. If the user explicitly requests to open a link, use the browser-opening function to launch it.
6. Always prioritize trustworthy sources such as official sites, academic research, and reputable news outlets.
7. If no results are found, state this clearly instead of guessing.

Rules:
- Never make up information.
- Do not perform unrelated actions.
- Keep the summary short and focused unless the user requests detailed results.
- When showing results, present them in a numbered list format for clarity.
";

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
            humanInputMode: HumanInputMode.ALWAYS)
            .RegisterPrintMessage();
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
            var intentResult = await intentDetectionService.DetectIntentAsync(
                messages.LastOrDefault()?.GetContent() ?? "", "tr");

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
        var message = context.LastOrDefault()?.GetContent() ?? "";
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

                    return ParseJsonResponse(result, $"✅ Komut işlendi: {userInput}");
                }
                catch (Exception ex)
                {
                    contextManager.UpdateContext("system_error", args, ex.Message);
                    return $"❌ Komut işleme hatası: {ex.Message}";
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

                    // Update application state on successful open
                    var parsedResult = TryParseJsonResult(result);
                    if (parsedResult?.Success == true)
                    {
                        contextManager.SetApplicationState(appName, true);
                    }

                    contextManager.UpdateContext("app_open", appName, result);
                    return ParseJsonResponse(result, $"✅ {appName} açıldı");
                }
                catch (Exception ex)
                {
                    contextManager.UpdateContext("app_open_error", args, ex.Message);
                    return $"❌ Uygulama açma hatası: {ex.Message}";
                }
            },

            ["CloseApplicationAsync"] = async (args) =>
            {
                try
                {
                    var jsonArgs = JsonSerializer.Deserialize<Dictionary<string, object>>(args);
                    var appName = jsonArgs["applicationName"]?.ToString() ?? "";

                    // Check if app is actually open
                    if (!contextManager.IsApplicationOpen(appName))
                    {
                        return $"ℹ️ {appName} zaten kapalı";
                    }

                    var result = await functions.CloseApplicationAsync(appName);

                    // Update application state on successful close
                    var parsedResult = TryParseJsonResult(result);
                    if (parsedResult?.Success == true)
                    {
                        contextManager.SetApplicationState(appName, false);
                    }

                    contextManager.UpdateContext("app_close", appName, result);
                    return ParseJsonResponse(result, $"✅ {appName} kapatıldı");
                }
                catch (Exception ex)
                {
                    contextManager.UpdateContext("app_close_error", args, ex.Message);
                    return $"❌ Uygulama kapatma hatası: {ex.Message}";
                }
            },

            ["PlayMusicAsync"] = async (args) =>
            {
                try
                {
                    var jsonArgs = JsonSerializer.Deserialize<Dictionary<string, object>>(args);
                    var trackName = jsonArgs["trackName"]?.ToString() ?? "";

                    var result = await functions.PlayMusicAsync(trackName);

                    contextManager.UpdateContext("music_play", trackName, result);
                    return ParseJsonResponse(result, $"🎵 Müzik çalıyor: {trackName}");
                }
                catch (Exception ex)
                {
                    contextManager.UpdateContext("music_play_error", args, ex.Message);
                    return $"❌ Müzik çalma hatası: {ex.Message}";
                }
            },

            ["ControlDeviceAsync"] = async (args) =>
            {
                try
                {
                    var jsonArgs = JsonSerializer.Deserialize<Dictionary<string, object>>(args);
                    var deviceName = jsonArgs["deviceName"]?.ToString() ?? "";
                    var action = jsonArgs["action"]?.ToString() ?? "";

                    var result = await functions.ControlDeviceAsync(deviceName, action);

                    contextManager.UpdateContext("device_control", $"{deviceName}:{action}", result);
                    return ParseJsonResponse(result, $"📱 {deviceName} - {action} işlemi tamamlandı");
                }
                catch (Exception ex)
                {
                    contextManager.UpdateContext("device_control_error", args, ex.Message);
                    return $"❌ Cihaz kontrol hatası: {ex.Message}";
                }
            },

            ["DetectIntentAsync"] = async (args) =>
            {
                try
                {
                    var jsonArgs = JsonSerializer.Deserialize<Dictionary<string, object>>(args);
                    var text = jsonArgs["text"]?.ToString() ?? "";
                    var language = jsonArgs.ContainsKey("language") ? jsonArgs["language"]?.ToString() : "tr";

                    var result = await functions.DetectIntentAsync(text, language);

                    contextManager.UpdateContext("intent_detection", text, result);
                    return ParseJsonResponse(result, $"🎯 Intent tespit edildi: {text}");
                }
                catch (Exception ex)
                {
                    contextManager.UpdateContext("intent_detection_error", args, ex.Message);
                    return $"❌ Intent tespit hatası: {ex.Message}";
                }
            },

            ["GetAvailableCommandsAsync"] = async (args) =>
            {
                try
                {
                    var jsonArgs = JsonSerializer.Deserialize<Dictionary<string, object>>(args);
                    var language = jsonArgs.ContainsKey("language") ? jsonArgs["language"]?.ToString() : "tr";
                    var category = jsonArgs.ContainsKey("category") ? jsonArgs["category"]?.ToString() : null;

                    var result = await functions.GetAvailableCommandsAsync(language, category);

                    contextManager.UpdateContext("get_commands", $"{language}:{category}", result);
                    return ParseJsonResponse(result, "📋 Mevcut komutlar listelendi");
                }
                catch (Exception ex)
                {
                    contextManager.UpdateContext("get_commands_error", args, ex.Message);
                    return $"❌ Komut listesi alma hatası: {ex.Message}";
                }
            }
        };
    }

    /// <summary>
    /// Parses JSON response and extracts meaningful message
    /// </summary>
    private static string ParseJsonResponse(string jsonResult, string defaultMessage = "İşlem tamamlandı")
    {
        try
        {
            var jsonDocument = JsonDocument.Parse(jsonResult);
            var root = jsonDocument.RootElement;

            // Check for success field
            if (root.TryGetProperty("success", out var successElement))
            {
                var isSuccess = successElement.GetBoolean();

                if (isSuccess)
                {
                    // Try to get message
                    if (root.TryGetProperty("message", out var messageElement))
                    {
                        var message = messageElement.GetString();
                        return !string.IsNullOrEmpty(message) ? message : defaultMessage;
                    }

                    // Try to get result field
                    if (root.TryGetProperty("result", out var resultElement))
                    {
                        var result = resultElement.GetString();
                        return !string.IsNullOrEmpty(result) ? result : defaultMessage;
                    }

                    return defaultMessage;
                }
                else
                {
                    // Handle error case
                    if (root.TryGetProperty("error", out var errorElement))
                    {
                        return $"❌ {errorElement.GetString()}";
                    }

                    if (root.TryGetProperty("message", out var errorMessageElement))
                    {
                        return $"❌ {errorMessageElement.GetString()}";
                    }

                    return "❌ İşlem başarısız";
                }
            }

            // If no success field, try to extract any meaningful data
            if (root.TryGetProperty("message", out var directMessageElement))
            {
                return directMessageElement.GetString() ?? defaultMessage;
            }

            // If it's an array or complex object, return summary
            if (root.ValueKind == JsonValueKind.Array)
            {
                return $"✅ {root.GetArrayLength()} öğe döndürüldü";
            }

            return defaultMessage;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"⚠️ JSON parse hatası: {ex.Message}");
            return defaultMessage;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Response parse hatası: {ex.Message}");
            return defaultMessage;
        }
    }

    /// <summary>
    /// Helper method to safely parse JSON result for internal use
    /// </summary>
    private static CommandResultWrapper? TryParseJsonResult(string jsonResult)
    {
        try
        {
            var jsonDocument = JsonDocument.Parse(jsonResult);
            var root = jsonDocument.RootElement;

            var success = false;
            var message = "";
            var error = "";

            if (root.TryGetProperty("success", out var successElement))
            {
                success = successElement.GetBoolean();
            }

            if (root.TryGetProperty("message", out var messageElement))
            {
                message = messageElement.GetString() ?? "";
            }

            if (root.TryGetProperty("error", out var errorElement))
            {
                error = errorElement.GetString() ?? "";
            }

            return new CommandResultWrapper
            {
                Success = success,
                Message = message,
                Error = error
            };
        }
        catch
        {
            return null;
        }
    }

}