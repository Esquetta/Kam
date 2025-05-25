namespace SmartVoiceAgent.Core.Entities;

/// <summary>
/// Represents a dynamically learned command by the AI agent.
/// </summary>
/// <param name="Id">Unique identifier of the learned command.</param>
/// <param name="CommandText">The text of the command learned.</param>
/// <param name="CreatedAt">Date and time when the command was learned.</param>
/// <param name="UsageCount">Number of times this command was used.</param>
public record LearnedCommand(
    Guid Id,
    string CommandText,
    DateTime CreatedAt,
    int UsageCount);
