namespace SmartVoiceAgent.Core.Entities;
public class CommandResult
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public object Data { get; set; }
    public string Error { get; set; }
    public string OriginalInput { get; set; }

}
