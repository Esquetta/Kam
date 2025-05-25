namespace SmartVoiceAgent.Core.Dtos
{
    /// <summary>
    /// Represents a voice command input received by the agent.
    /// </summary>
    public record VoiceCommandDTO(string Text, DateTime ReceivedAt);
}
