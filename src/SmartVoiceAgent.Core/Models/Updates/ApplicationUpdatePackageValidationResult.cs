namespace SmartVoiceAgent.Core.Models.Updates;

public sealed record ApplicationUpdatePackageValidationResult(
    bool CanRestart,
    string Message,
    string? NormalizedPackagePath = null);
