

using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Services;
/// <summary>
/// Dispatches queries to their corresponding handlers.
/// </summary>

public sealed class CommandBus(IServiceProvider serviceProvider) : ICommandBus
{
    private readonly IServiceProvider serviceProvider = serviceProvider;


    public async Task<TResult> SendAsync<TCommand, TResult>(TCommand command)
    {
        var handler = serviceProvider.GetService(typeof(ICommandHandler<TCommand, TResult>))
            as ICommandHandler<TCommand, TResult>;

        if (handler is null)
            throw new InvalidOperationException($"No handler registered for command type {typeof(TCommand).Name}");

        return await handler.HandleAsync(command);
    }
}
