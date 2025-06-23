using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Application.Notifications;

namespace SmartVoiceAgent.Application.Handlers;

/// <summary>
/// Handles the ControlDeviceCommand by performing the specified action on a device.
/// </summary>
public class ControlDeviceCommandHandler : IRequestHandler<ControlDeviceCommand, CommandResultDTO>
{
    private readonly IMediator _mediator;

    public ControlDeviceCommandHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<CommandResultDTO> Handle(ControlDeviceCommand request, CancellationToken cancellationToken)
    {
        // TODO: Implement device control logic.
        await _mediator.Publish(new DeviceControlledNotification(request.Action, request.DeviceName), cancellationToken);
        await Task.CompletedTask;
        return new CommandResultDTO(true, $"{request.Action} performed on {request.DeviceName}.");
    }
}
