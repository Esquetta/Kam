namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Represents a handler for a command of type <typeparamref name="TCommand"/>.
/// </summary>
/// <typeparam name="TCommand">The type of command to handle.</typeparam>
public interface ICommandHandler<TCommand>
{
    /// <summary>
    /// Handles the specified command asynchronously.
    /// </summary>
    /// <param name="command">The command instance to handle.</param>
    Task HandleAsync(TCommand command);
}
