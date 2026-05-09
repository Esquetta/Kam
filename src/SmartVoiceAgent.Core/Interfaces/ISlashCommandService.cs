using SmartVoiceAgent.Core.Models.SlashCommands;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ISlashCommandService
{
    IReadOnlyList<SlashCommandDefinition> GetCommands();

    IReadOnlyList<SlashCommandDefinition> GetSuggestions(string input);

    bool IsSlashCommand(string input);

    Task<SlashCommandResult> ExecuteAsync(
        string input,
        CancellationToken cancellationToken = default);
}
