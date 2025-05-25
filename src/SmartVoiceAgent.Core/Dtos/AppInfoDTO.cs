namespace SmartVoiceAgent.Core.Dtos
{
    /// <summary>
    /// Represents basic information about an installed or running application.
    /// </summary>
    public record AppInfoDTO(string Name,string Path,bool IsRuning);
    
}
