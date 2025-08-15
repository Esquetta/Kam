namespace SmartVoiceAgent.Core.Dtos;
/// <summary>
/// Agent'a döndürülecek yanıt modeli.
/// </summary>
public record AgentApplicationResponse(
    bool Success,
    string Message,
    string ApplicationName = null,
    string ExecutablePath = null,
    bool IsInstalled = false,
    bool IsRunning = false
);
