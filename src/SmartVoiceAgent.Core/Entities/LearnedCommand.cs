namespace SmartVoiceAgent.Core.Entities;

/// <summary>
/// Represents a learned command with metadata.
/// </summary>
public record LearnedCommand(
    Guid Id,
    string CommandText,
    DateTime CreatedAt,
    int UsageCount,
    string? Description = null,
    string? Pattern = null,
    string? ExpectedResponse = null,
    string? Category = null,
    Dictionary<string, object>? Parameters = null,
    bool IsActive = true,
    DateTime? LastUsedAt = null,
    string? LearnedFrom = null,
    int Priority = 0)
{
    public Dictionary<string, object> Parameters { get; init; } = Parameters ?? new();
}
