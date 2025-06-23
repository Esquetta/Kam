using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Application.Notifications;

namespace SmartVoiceAgent.Application.Handlers;

/// <summary>
/// Handles the SendMessageCommand by sending a message to the specified recipient.
/// </summary>
public class SendMessageCommandHandler(IMediator _mediator) : IRequestHandler<SendMessageCommand, CommandResultDTO>
{
    private readonly IMediator _mediator;
    public async Task<CommandResultDTO> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        // TODO: Implement message sending logic.
        await _mediator.Publish(new MessageSentNotification(request.Recipient, request.Message), cancellationToken);
        await Task.CompletedTask;
        return new CommandResultDTO(true, $"Message sent to {request.Recipient}."); ;
    }
}
