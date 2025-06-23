using MediatR;
using SmartVoiceAgent.Application.NotificationHandlers;
using SmartVoiceAgent.Core.Commands;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers.Commands;

/// <summary>
/// Handles the command to open an application.
/// </summary>
public sealed class OpenApplicationCommandHandler(IApplicationService appService, IMediator _mediator) : IRequestHandler<OpenApplicationCommand, CommandResultDTO>
{
    private readonly IApplicationService _appService = appService;
    private readonly IMediator _mediator;


    public async Task<CommandResultDTO> Handle(OpenApplicationCommand request, CancellationToken cancellationToken)
    {
        // TODO: Implement device control logic.
        await _mediator.Publish(new ApplicationOpenedNotification(request.ApplicationName), cancellationToken);
        await Task.CompletedTask;
        return new CommandResultDTO(true, $"{request.ApplicationName} application starting...");
    }
}
