namespace SmartVoiceAgent.Core.Entities;
public class CommandResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public object Data { get; set; } = new();
    public string Error { get; set; } = string.Empty;
    public string OriginalInput { get; set; } = string.Empty;

}
