using AutoGen.Core;

namespace SmartVoiceAgent.Infrastructure.AutoGen.Middlewares;
/// <summary>
/// Enhanced logging middleware specifically for conversation flow
/// </summary>
public class ConversationFlowMiddleware : IMiddleware
{
    private static readonly object _lockObject = new();
    private static int _conversationStep = 0;

    public string? Name => "ConversationFlowMiddleware";

    public async Task<IMessage> InvokeAsync(
        MiddlewareContext context,
        IAgent agent,
        CancellationToken cancellationToken = default)
    {
        lock (_lockObject)
        {
            _conversationStep++;
        }

        var stepNumber = _conversationStep;

        // Log conversation flow
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"🔄 STEP {stepNumber}: [{agent.Name}] is processing...");
        Console.ResetColor();

        var lastMessage = context.Messages.LastOrDefault();
        if (lastMessage != null)
        {
            Console.WriteLine($"   📨 Input from: {lastMessage.From ?? "Unknown"}");
            var content = lastMessage.GetContent()?.Trim() ?? "";
            if (content.Length > 100)
            {
                content = content.Substring(0, 100) + "...";
            }
            Console.WriteLine($"   💬 Content: {content}");
        }

        var startTime = DateTime.UtcNow;

        try
        {
            var result = await agent.GenerateReplyAsync(context.Messages, context.Options, cancellationToken);
            var responseTime = DateTime.UtcNow - startTime;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✅ STEP {stepNumber} COMPLETED: [{agent.Name}] responded in {responseTime.TotalMilliseconds:F0}ms");
            Console.ResetColor();

            var responseContent = result.GetContent()?.Trim() ?? "";
            if (responseContent.Length > 150)
            {
                responseContent = responseContent.Substring(0, 150) + "...";
            }
            Console.WriteLine($"   💭 Response: {responseContent}");

            return result;
        }
        catch (Exception ex)
        {
            var responseTime = DateTime.UtcNow - startTime;

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ STEP {stepNumber} FAILED: [{agent.Name}] error after {responseTime.TotalMilliseconds:F0}ms");
            Console.WriteLine($"   🚨 Error: {ex.Message}");
            Console.ResetColor();

            throw;
        }
    }
}
