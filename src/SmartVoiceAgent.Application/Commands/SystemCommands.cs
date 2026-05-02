using SmartVoiceAgent.Core.Entities;

namespace SmartVoiceAgent.Application.Commands
{
   
    public record ControlSystemVolumeCommand(string Action, int Level = 50) : ICommand<CommandResult>;

    
    public record ControlScreenBrightnessCommand(string Action, int Level = 50) : ICommand<CommandResult>;

   
    public record ControlWiFiCommand(string Action) : ICommand<CommandResult>;

    
    public record ControlBluetoothCommand(string Action) : ICommand<CommandResult>;

    
    public record ControlSystemPowerCommand(string Action, int DelayMinutes = 0) : ICommand<CommandResult>;

    
    public record GetSystemStatusCommand(string InfoType = "all") : ICommand<CommandResult>;

}
