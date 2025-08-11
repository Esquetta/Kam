using AutoGen.Core;

namespace SmartVoiceAgent.Infrastructure.AutoGen.Middlewares;

/// <summary>
/// Detailed conversation debugging middleware for monitoring agent interactions
/// </summary>
public class ConversationDebugMiddleware : IMiddleware
{
    private readonly bool _enableDetailedLogging;
    private readonly bool _logMessageContent;
    private readonly bool _logPerformanceMetrics;
    private static int _messageCounter = 0;

    public ConversationDebugMiddleware(
        bool enableDetailedLogging = true,
        bool logMessageContent = true,
        bool logPerformanceMetrics = true)
    {
        _enableDetailedLogging = enableDetailedLogging;
        _logMessageContent = logMessageContent;
        _logPerformanceMetrics = logPerformanceMetrics;
    }

    public string? Name => "ConversationDebugMiddleware";

    public async Task<IMessage> InvokeAsync(
        MiddlewareContext context,
        IAgent agent,
        CancellationToken cancellationToken = default)
    {
        var messageId = ++_messageCounter;
        var startTime = DateTime.UtcNow;

        if (_enableDetailedLogging)
        {
            LogIncomingContext(agent, context, messageId);
        }

        try
        {
            // Execute the agent's response
            var result = await agent.GenerateReplyAsync(context.Messages, context.Options, cancellationToken);

            var endTime = DateTime.UtcNow;
            var responseTime = endTime - startTime;

            if (_enableDetailedLogging)
            {
                LogAgentResponse(agent, result, messageId, responseTime);
            }

            if (_logPerformanceMetrics)
            {
                LogPerformanceMetrics(agent, responseTime, true);
            }

            return result;
        }
        catch (Exception ex)
        {
            var endTime = DateTime.UtcNow;
            var responseTime = endTime - startTime;

            LogAgentError(agent, ex, messageId, responseTime);

            if (_logPerformanceMetrics)
            {
                LogPerformanceMetrics(agent, responseTime, false);
            }

            throw;
        }
    }

    private void LogIncomingContext(IAgent agent, MiddlewareContext context, int messageId)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"┌─ 📥 INCOMING TO [{agent.Name}] (MSG #{messageId}) ─┐");
        Console.ResetColor();

        Console.WriteLine($"│ Time: {DateTime.UtcNow:HH:mm:ss.fff}");
        Console.WriteLine($"│ Message Count: {context.Messages.Count()}");

        if (_logMessageContent)
        {
            var lastMessage = context.Messages.LastOrDefault();
            if (lastMessage != null)
            {
                Console.WriteLine($"│ From: {lastMessage.From ?? "Unknown"}");
                Console.WriteLine($"│ Role: {lastMessage.GetRole()}");

                var content = lastMessage.GetContent()?.Trim() ?? "";
                if (content.Length > 200)
                {
                    content = content.Substring(0, 200) + "...";
                }
                Console.WriteLine($"│ Content: {content}");

                // Log Tool Calls if present
                if (lastMessage is ToolCallMessage toolCallMsg)
                {
                    var toolCalls = toolCallMsg.ToolCalls;
                    if (toolCalls?.Any() == true)
                    {
                        Console.WriteLine($"│ Tool Calls: {toolCalls.Count}");
                        foreach (var call in toolCalls)
                        {
                            Console.WriteLine($"│   - {call.FunctionName}({TruncateArgs(call.FunctionArguments)})");
                        }
                    }
                }
            }
        }

        Console.WriteLine($"└─────────────────────────────────────────────────┘");
    }

    private void LogAgentResponse(IAgent agent, IMessage result, int messageId, TimeSpan responseTime)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"┌─ 📤 RESPONSE FROM [{agent.Name}] (MSG #{messageId}) ─┐");
        Console.ResetColor();

        Console.WriteLine($"│ Time: {DateTime.UtcNow:HH:mm:ss.fff}");
        Console.WriteLine($"│ Response Time: {responseTime.TotalMilliseconds:F0}ms");
        Console.WriteLine($"│ Role: {result.GetRole()}");

        if (_logMessageContent)
        {
            var content = result.GetContent()?.Trim() ?? "";
            if (content.Length > 300)
            {
                content = content.Substring(0, 300) + "...";
            }
            Console.WriteLine($"│ Content: {content}");
        }

        // Log Tool Calls if present
        if (result is ToolCallMessage toolCallMsg)
        {
            var toolCalls = toolCallMsg.ToolCalls;
            if (toolCalls?.Any() == true)
            {
                Console.WriteLine($"│ Tool Calls Made: {toolCalls.Count}");
                foreach (var call in toolCalls)
                {
                    Console.WriteLine($"│   - {call.FunctionName} ( {TruncateArgs(call.FunctionArguments)})");
                }
            }
        }

        Console.WriteLine($"└─────────────────────────────────────────────────┘");
    }

    private void LogAgentError(IAgent agent, Exception ex, int messageId, TimeSpan responseTime)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"┌─ ❌ ERROR IN [{agent.Name}] (MSG #{messageId}) ─┐");
        Console.ResetColor();

        Console.WriteLine($"│ Time: {DateTime.UtcNow:HH:mm:ss.fff}");
        Console.WriteLine($"│ Response Time: {responseTime.TotalMilliseconds:F0}ms");
        Console.WriteLine($"│ Error Type: {ex.GetType().Name}");
        Console.WriteLine($"│ Error Message: {ex.Message}");

        if (ex.InnerException != null)
        {
            Console.WriteLine($"│ Inner Exception: {ex.InnerException.Message}");
        }

        Console.WriteLine($"└─────────────────────────────────────────────────┘");
    }

    private void LogPerformanceMetrics(IAgent agent, TimeSpan responseTime, bool success)
    {
        var color = success ? ConsoleColor.Green : ConsoleColor.Red;
        var icon = success ? "✅" : "❌";
        var status = success ? "SUCCESS" : "FAILED";

        Console.ForegroundColor = color;
        Console.WriteLine($"⚡ [{agent.Name}] {icon} {status} - {responseTime.TotalMilliseconds:F0}ms");
        Console.ResetColor();
    }

    private string TruncateArgs(string? arguments)
    {
        if (string.IsNullOrEmpty(arguments)) return "()";
        if (arguments.Length > 50)
        {
            return arguments.Substring(0, 50) + "...";
        }
        return arguments;
    }
}
