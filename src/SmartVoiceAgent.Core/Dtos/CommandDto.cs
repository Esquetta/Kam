namespace SmartVoiceAgent.Core.Dtos
{
    /// <summary>
    /// Represents a learned or executable command with its details.
    /// </summary>
    public record CommandDto(string Name,string Action,string Description);
}
