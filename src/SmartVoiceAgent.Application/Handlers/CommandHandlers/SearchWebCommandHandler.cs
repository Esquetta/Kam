using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Application.Notifications;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers;

/// <summary>
/// Handles the SearchWebCommand by performing a web search.
/// </summary>
public class SearchWebCommandHandler : IRequestHandler<SearchWebCommand, CommandResultDTO>
{
    private readonly IMediator _mediator;
    private readonly IWebResearchService webResearchService;

    public SearchWebCommandHandler(IMediator mediator, IWebResearchService webResearchService)
    {
        _mediator = mediator;
        this.webResearchService = webResearchService;
    }

    public async Task<CommandResultDTO> Handle(SearchWebCommand request, CancellationToken cancellationToken)
    {
        await webResearchService.SearchAndOpenAsync(new Core.Models.WebResearchRequest { Language = request.lang, Query = request.Query, MaxResults = request.results });
        await _mediator.Publish(new WebSearchedNotification(request.Query), cancellationToken);
        await Task.CompletedTask;
        return new CommandResultDTO(true, $"Search performed for '{request.Query}'.");
    }
}
