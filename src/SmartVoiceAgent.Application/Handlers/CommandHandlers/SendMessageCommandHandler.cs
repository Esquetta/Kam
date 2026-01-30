using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Application.Notifications;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers;

/// <summary>
/// Handles the SendMessageCommand by sending a message to the specified recipient.
/// </summary>
public class SendMessageCommandHandler : IRequestHandler<SendMessageCommand, CommandResultDTO>
{
    private readonly IMessageServiceFactory _messageServiceFactory;
    private readonly IMediator _mediator;

    public SendMessageCommandHandler(IMessageServiceFactory messageServiceFactory, IMediator mediator)
    {
        _messageServiceFactory = messageServiceFactory;
        _mediator = mediator;
    }

    public async Task<CommandResultDTO> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Get the appropriate message service for the recipient
            var messageService = _messageServiceFactory.GetService(request.Recipient);
            
            // Send the message
            var success = await messageService.SendMessageAsync(
                request.Recipient, 
                request.Message,
                subject: null, // Could be extracted from message or added to command
                cancellationToken);
            
            if (success)
            {
                // Publish notification
                await _mediator.Publish(
                    new MessageSentNotification(request.Recipient, request.Message), 
                    cancellationToken);
                
                return new CommandResultDTO(true, $"✅ Message sent to {request.Recipient}");
            }
            else
            {
                return new CommandResultDTO(false, $"❌ Failed to send message to {request.Recipient}");
            }
        }
        catch (NotSupportedException ex)
        {
            return new CommandResultDTO(false, ex.Message);
        }
        catch (Exception ex)
        {
            return new CommandResultDTO(false, $"❌ Error sending message: {ex.Message}");
        }
    }
}
