using SmartVoiceAgent.Core.Commands;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers.Commands;

/// <summary>
/// Handles the command to open an application.
/// </summary>
public sealed class OpenApplicationCommandHandler(IApplicationService appService)
    : ICommandHandler<OpenApplicationCommand, CommandResultDTO>
{
    private readonly IApplicationService _appService = appService;

    public async Task<CommandResultDTO> HandleAsync(OpenApplicationCommand command)
    {
        await _appService.OpenApplicationAsync(command.ApplicationName);

        return new CommandResultDTO(true, $"Application {command.ApplicationName} started.");
    }
}
