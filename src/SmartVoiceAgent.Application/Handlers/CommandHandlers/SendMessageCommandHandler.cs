using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers;

/// <summary>
/// Handles the SendMessageCommand by sending a message to the specified recipient.
/// </summary>
public class SendMessageCommandHandler : ICommandHandler<SendMessageCommand>
{
    public async Task<CommandResultDTO> HandleAsync(SendMessageCommand command)
    {
        // TODO: Implement message sending logic.
        await Task.CompletedTask;
        return new CommandResultDTO(true, $"Message sent to {command.Recipient}.");
    }
}
