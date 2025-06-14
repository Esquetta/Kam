using MediatR;
using MongoDB.Driver;

namespace SmartVoiceAgent.Application.Handlers;

/// <summary>
/// Handles the PlayMusicCommand by playing the requested track.
/// </summary>
public class PlayMusicCommandHandler : IRequestHandler<PlayMusicCommand, CommandResultDTO>
{
    public async Task<CommandResultDTO> Handle(PlayMusicCommand request, CancellationToken cancellationToken)
    {
        // TODO: Implement music playing logic.
        await Task.CompletedTask;
        return new CommandResultDTO(true, $"Playing {request.TrackName}.");
    }
}
