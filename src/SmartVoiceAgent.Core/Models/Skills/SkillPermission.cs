namespace SmartVoiceAgent.Core.Models.Skills;

public enum SkillPermission
{
    None = 0,
    ProcessLaunch = 1,
    ProcessControl = 2,
    FileSystemRead = 3,
    FileSystemWrite = 4,
    Network = 5
}
