using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers;

/// <summary>
/// Handles the PlayMusicCommand by playing the requested track.
/// </summary>
public class PlayMusicCommandHandler : ICommandHandler<PlayMusicCommand, CommandResultDTO>
{
    public async Task<CommandResultDTO> HandleAsync(PlayMusicCommand command)
    {
        // TODO: Implement music playing logic.
        await Task.CompletedTask;
        return new CommandResultDTO(true, $"Playing {command.TrackName}.");
    }
}
