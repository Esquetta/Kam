using MediatR;
using SmartVoiceAgent.Core.Entities;

namespace SmartVoiceAgent.Application.Commands
{
   
    public record ControlSystemVolumeCommand(string Action, int Level = 50) : IRequest<CommandResult>;

    
    public record ControlScreenBrightnessCommand(string Action, int Level = 50) : IRequest<CommandResult>;

   
    public record ControlWiFiCommand(string Action) : IRequest<CommandResult>;

    
    public record ControlBluetoothCommand(string Action) : IRequest<CommandResult>;

    
    public record ControlSystemPowerCommand(string Action, int DelayMinutes = 0) : IRequest<CommandResult>;

    
    public record GetSystemStatusCommand(string InfoType = "all") : IRequest<CommandResult>;

}
