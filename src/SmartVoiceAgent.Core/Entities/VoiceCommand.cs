namespace SmartVoiceAgent.Core.Entities
{


    /// <summary>
    /// Represents a voice command given by the user and its details.
    /// </summary>
    public record VoiceCommand(string Text, DateTime ReceivedAt, string? ExecutedCommand = null);

}
