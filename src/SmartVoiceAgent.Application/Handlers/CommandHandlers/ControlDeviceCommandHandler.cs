using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers;

/// <summary>
/// Handles the ControlDeviceCommand by performing the specified action on a device.
/// </summary>
public class ControlDeviceCommandHandler : ICommandHandler<ControlDeviceCommand>
{
    public async Task<CommandResultDTO> HandleAsync(ControlDeviceCommand command)
    {
        // TODO: Implement device control logic.
        await Task.CompletedTask;
        return new CommandResultDTO(true, $"{command.Action} performed on {command.DeviceName}.");
    }
}
