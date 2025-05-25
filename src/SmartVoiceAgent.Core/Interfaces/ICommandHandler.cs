namespace SmartVoiceAgent.Core.Interfaces;

public interface ICommandHandler<TCommand>
{
    /// <summary>
    /// Handles the specified command asynchronously.
    /// </summary>
    /// <param name="command">The command instance to handle.</param>
    /// <returns>Result of command execution.</returns>
    Task<CommandResultDTO> HandleAsync(TCommand command);
}

