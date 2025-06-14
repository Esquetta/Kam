using MediatR;
using SmartVoiceAgent.Application.Commands;

namespace SmartVoiceAgent.Application.Handlers;

/// <summary>
/// Handles the SearchWebCommand by performing a web search.
/// </summary>
public class SearchWebCommandHandler : IRequestHandler<SearchWebCommand, CommandResultDTO>
{
    public async Task<CommandResultDTO> Handle(SearchWebCommand request, CancellationToken cancellationToken)
    {
        // TODO: Implement web search logic.
        await Task.CompletedTask;
        return new CommandResultDTO(true, $"Search performed for '{request.Query}'.");
    }
}
