namespace SmartVoiceAgent.Core.Models.SlashCommands;

public sealed record SlashCommandDefinition(
    string Name,
    string Summary,
    string Usage,
    string Category,
    IReadOnlyList<string>? Aliases = null)
{
    public IReadOnlyList<string> Aliases { get; init; } = Aliases ?? [];
}
