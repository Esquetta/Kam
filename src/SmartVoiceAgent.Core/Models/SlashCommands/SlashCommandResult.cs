namespace SmartVoiceAgent.Core.Models.SlashCommands;

public sealed record SlashCommandResult(
    bool Success,
    string Message,
    string CommandName)
{
    public static SlashCommandResult Succeeded(string commandName, string message)
    {
        return new SlashCommandResult(true, message, commandName);
    }

    public static SlashCommandResult Failed(string commandName, string message)
    {
        return new SlashCommandResult(false, message, commandName);
    }
}
