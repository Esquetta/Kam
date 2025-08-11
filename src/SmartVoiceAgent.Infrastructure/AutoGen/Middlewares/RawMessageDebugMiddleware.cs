using AutoGen.Core;

namespace SmartVoiceAgent.Infrastructure.AutoGen.Middlewares;
public class RawMessageDebugMiddleware : IMiddleware
{
    public string? Name => "RawMessageDebugMiddleware";

    public async Task<IMessage> InvokeAsync(MiddlewareContext context, IAgent agent, CancellationToken cancellationToken = default)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[DEBUG:{agent.Name}] Raw incoming messages:");
        foreach (var msg in context.Messages)
        {
            Console.WriteLine($"From: {msg.From}, Role: {msg.GetRole()}, Content: {msg.GetContent()}");
        }
        Console.ResetColor();

        return await agent.GenerateReplyAsync(context.Messages, context.Options, cancellationToken);
    }
}
