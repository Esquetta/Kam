using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Application.Notifications;

namespace SmartVoiceAgent.Application.Handlers;

/// <summary>
/// Handles the SearchWebCommand by performing a web search.
/// </summary>
public class SearchWebCommandHandler : IRequestHandler<SearchWebCommand, CommandResultDTO>
{
    private readonly IMediator _mediator;

    public SearchWebCommandHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<CommandResultDTO> Handle(SearchWebCommand request, CancellationToken cancellationToken)
    {
        // TODO: Implement web search logic.
        await _mediator.Publish(new WebSearchedNotification(request.Query), cancellationToken);
        await Task.CompletedTask;
        return new CommandResultDTO(true, $"Search performed for '{request.Query}'.");
    }
}
