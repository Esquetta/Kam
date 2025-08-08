using AutoGen.Core;

namespace SmartVoiceAgent.Infrastructure.Middlewares;
/// <summary>
/// Context-aware middleware for agents
/// </summary>
public class ContextAwareMiddleware : IMiddleware
{
    private readonly ConversationContextManager _contextManager;

    public ContextAwareMiddleware(ConversationContextManager contextManager)
    {
        _contextManager = contextManager;
    }

    public string? Name => "ContextAwareMiddleware";

    public async Task<IMessage> InvokeAsync(
        MiddlewareContext context,
        IAgent agent,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            // Pre-process: Add context information
            var lastMessage = context.Messages.LastOrDefault();
            if (lastMessage != null)
            {
                var relevantContext = _contextManager.GetRelevantContext(lastMessage.GetContent() ?? "");
                if (!string.IsNullOrEmpty(relevantContext))
                {
                    Console.WriteLine($"🧠 Context for {agent.Name}: {relevantContext}");
                }
            }

            // Process message
            var result = await agent.GenerateReplyAsync(context.Messages, context.Options, cancellationToken);

            // Post-process: Update context
            var responseTime = DateTime.UtcNow - startTime;

            if (lastMessage != null && result != null)
            {
                _contextManager.UpdateContext(
                    agent.Name,
                    lastMessage.GetContent() ?? "",
                    result.GetContent() ?? "");
            }

            Console.WriteLine($"⚡ {agent.Name} responded in {responseTime.TotalMilliseconds:F0}ms");

            return result;
        }
        catch (Exception ex)
        {
            var responseTime = DateTime.UtcNow - startTime;
            Console.WriteLine($"❌ {agent.Name} failed after {responseTime.TotalMilliseconds:F0}ms: {ex.Message}");
            throw;
        }
    }
}