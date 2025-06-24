using MediatR;
using SmartVoiceAgent.Application.NotificationHandlers;
using SmartVoiceAgent.Core.Commands;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers;
/// <summary>
/// Handles the command to open an application.
/// </summary>
public sealed class OpenApplicationCommandHandler : IRequestHandler<OpenApplicationCommand, CommandResultDTO>
{
    private readonly IMediator _mediator;
    private readonly IApplicationServiceFactory _factory;
    public OpenApplicationCommandHandler(IMediator mediator, IApplicationServiceFactory factory)
    {
        _mediator = mediator;
        _factory = factory;
    }

    public async Task<CommandResultDTO> Handle(OpenApplicationCommand request, CancellationToken cancellationToken)
    {

        var appService = _factory.Create();
        await appService.OpenApplicationAsync(request.ApplicationName);
        await _mediator.Publish(new ApplicationOpenedNotification(request.ApplicationName), cancellationToken);
        await Task.CompletedTask;
        return new CommandResultDTO(true, $"{request.ApplicationName} application starting...");
    }
}