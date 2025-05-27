using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers;

/// <summary>
/// Handles the SearchWebCommand by performing a web search.
/// </summary>
public class SearchWebCommandHandler : ICommandHandler<SearchWebCommand>
{
    public async Task<CommandResultDTO> HandleAsync(SearchWebCommand command)
    {
        // TODO: Implement web search logic.
        await Task.CompletedTask;
        return new CommandResultDTO(true, $"Search performed for '{command.Query}'.");
    }
}
