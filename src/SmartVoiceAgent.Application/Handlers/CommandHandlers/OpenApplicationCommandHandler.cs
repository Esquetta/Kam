using MediatR;
using SmartVoiceAgent.Core.Commands;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers.Commands;

/// <summary>
/// Handles the command to open an application.
/// </summary>
public sealed class OpenApplicationCommandHandler(IApplicationService appService): IRequestHandler<OpenApplicationCommand, CommandResultDTO>
{
    private readonly IApplicationService _appService = appService;

    public async Task<CommandResultDTO> Handle(OpenApplicationCommand request, CancellationToken cancellationToken)
    {
        // TODO: Implement device control logic.
        await Task.CompletedTask;
        return new CommandResultDTO(true, $"{request.ApplicationName} application starting...");
    }
}
