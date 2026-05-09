namespace SmartVoiceAgent.Core.Models.Updates;

public sealed record ApplicationRestartPlan(
    bool CanRestart,
    string Message,
    string? ExecutablePath,
    string? UpdatePackagePath,
    IReadOnlyList<string> Steps);
