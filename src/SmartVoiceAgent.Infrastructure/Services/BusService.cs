using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Concrete implementation for dispatching commands.
/// </summary>
public class CommandBus : ICommandBus
{
    private readonly IServiceProvider _serviceProvider;

    public CommandBus(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<TResult> SendAsync<TCommand, TResult>(TCommand command)
    {
        var handler = _serviceProvider.GetService(typeof(ICommandHandler<TCommand,TResult>)) as ICommandHandler<TCommand, TResult>;

        if (handler is null)
            throw new InvalidOperationException($"No command handler registered for {typeof(TCommand).Name}");

        var result = await handler.HandleAsync(command);
        return (TResult)(object)result!;
    }
}
