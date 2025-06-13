using SmartVoiceAgent.Core.Contracts;

namespace SmartVoiceAgent.Application.Commands;

public record ControlDeviceCommand(string DeviceName, string Action) : ICommand<CommandResultDTO>;

