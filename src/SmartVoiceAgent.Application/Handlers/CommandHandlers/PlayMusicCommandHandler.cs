using MediatR;
using SmartVoiceAgent.Application.Notifications;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers;

/// <summary>
/// Handles the PlayMusicCommand by playing the requested track.
/// </summary>
public class PlayMusicCommandHandler : IRequestHandler<PlayMusicCommand, CommandResultDTO>
{
    private readonly IMusicService _musicService;
    private readonly IMediator _mediator;

    public PlayMusicCommandHandler(IMusicService musicService, IMediator mediator)
    {
        _musicService = musicService;
        _mediator = mediator;
    }

    public async Task<CommandResultDTO> Handle(PlayMusicCommand request, CancellationToken cancellationToken)
    {
        await _musicService.PlayMusicAsync(request.TrackName);

        await _mediator.Publish(new MusicPlayedNotification(request.TrackName), cancellationToken);

        return new CommandResultDTO(true, $"Playing {request.TrackName}.");
    }
}
