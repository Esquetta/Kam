namespace SmartVoiceAgent.Core.Models.Updates;

public sealed record ApplicationUpdateAsset(
    string Name,
    string DownloadUrl,
    long SizeBytes,
    string? ContentType,
    string? ChecksumName = null,
    string? ChecksumDownloadUrl = null);
