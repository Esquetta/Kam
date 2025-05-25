namespace SmartVoiceAgent.Core.Entities
{
    /// <summary>
    /// Represents a command that the agent has learned to recognize and execute.
    /// </summary>
    public record LearnedCommand(string Name, string Action, string Description);

}
