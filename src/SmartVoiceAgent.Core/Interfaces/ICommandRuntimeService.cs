using SmartVoiceAgent.Core.Models.Commands;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ICommandRuntimeService
{
    Task<CommandRuntimeResult> ExecuteAsync(string command, CancellationToken cancellationToken = default);
}
