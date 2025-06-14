using MediatR;
using SmartVoiceAgent.Application.Commands;

namespace SmartVoiceAgent.Application.Handlers;

/// <summary>
/// Handles the ControlDeviceCommand by performing the specified action on a device.
/// </summary>
public class ControlDeviceCommandHandler : IRequestHandler<ControlDeviceCommand, CommandResultDTO>
{
    public async Task<CommandResultDTO> Handle(ControlDeviceCommand request, CancellationToken cancellationToken)
    {
        // TODO: Implement device control logic.
        await Task.CompletedTask;
        return new CommandResultDTO(true, $"{request.Action} performed on {request.DeviceName}.");
    }
}
