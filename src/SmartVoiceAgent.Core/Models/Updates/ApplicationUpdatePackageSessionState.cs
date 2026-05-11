namespace SmartVoiceAgent.Core.Models.Updates;

public sealed record ApplicationUpdatePackageSessionState(
    string FilePath,
    string? Version,
    long? SizeBytes,
    string VerificationStatus,
    string? ExpectedSha256,
    string? ActualSha256,
    DateTimeOffset RecordedAt);
