namespace SmartVoiceAgent.Core.Models;
/// <summary>
/// Wrapper class for command results
/// </summary>
public class CommandResultWrapper
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string Error { get; set; } = "";
}