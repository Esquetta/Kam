using SmartVoiceAgent.Core.Entities;

namespace SmartVoiceAgent.Core.Interfaces;


public interface ICommandHandlerService
{
    Task<CommandResult> ExecuteCommandAsync(DynamicCommandRequest request);
    Task<List<AvailableCommand>> GetAvailableCommandsAsync(string language, string category = null);
}
