namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Dispatches commands to their corresponding handlers.
/// </summary>
public interface ICommandBus
{
    /// <summary>
    /// Dispatches the specified command to its handler asynchronously.
    /// </summary>
    /// <typeparam name="TCommand">The type of command.</typeparam>
    /// <typeparam name="TResult">The type of result expected.</typeparam>
    /// <param name="command">The command instance to dispatch.</param>
    /// <returns>The result of command execution.</returns>
    Task<TResult> SendAsync<TCommand, TResult>(TCommand command);
}
