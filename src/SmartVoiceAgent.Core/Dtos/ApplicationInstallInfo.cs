namespace SmartVoiceAgent.Core.Dtos;
/// <summary>
/// Contains information about application installation status.
/// </summary>
public record ApplicationInstallInfo(
    bool IsInstalled,
    string ExecutablePath,
    string DisplayName,
    string Version = null,
    DateTime? InstallDate = null
);
