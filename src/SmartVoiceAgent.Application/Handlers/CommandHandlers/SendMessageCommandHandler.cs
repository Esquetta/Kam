using MediatR;
using SmartVoiceAgent.Application.Commands;

namespace SmartVoiceAgent.Application.Handlers;

/// <summary>
/// Handles the SendMessageCommand by sending a message to the specified recipient.
/// </summary>
public class SendMessageCommandHandler : IRequestHandler<SendMessageCommand, CommandResultDTO>
{
    public async Task<CommandResultDTO> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        // TODO: Implement message sending logic.
        await Task.CompletedTask;
        return new CommandResultDTO(true, $"Message sent to {request.Recipient}."); ;
    }
}
