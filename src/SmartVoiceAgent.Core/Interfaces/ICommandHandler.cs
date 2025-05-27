namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Represents a handler for a command of type <typeparamref name="TCommand"/>.
/// </summary>
/// <typeparam name="TCommand">The type of command to handle.</typeparam>
/// <typeparam name="TResult">The type of result returned by the command.</typeparam>
public interface ICommandHandler<TCommand, TResult>
{
    /// <summary>
    /// Handles the specified command asynchronously.
    /// </summary>
    /// <param name="command">The command instance to handle.</param>
    /// <returns>The result of command execution.</returns>
    Task<TResult> HandleAsync(TCommand command);
}
