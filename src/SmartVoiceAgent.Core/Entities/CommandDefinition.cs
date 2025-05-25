namespace SmartVoiceAgent.Core.Entities;

/// <summary>
/// Represents the definition of a voice command with its associated action.
/// </summary>
/// <param name="Id">Unique identifier of the command.</param>
/// <param name="Name">The name of the command.</param>
/// <param name="CommandText">The voice command text.</param>
/// <param name="Action">The action to perform when command is triggered.</param>
/// <param name="CommandType">Type of the command (e.g., System, UserDefined).</param>
public record CommandDefinition(
    Guid Id,
    string Name,
    string CommandText,
    string Action,
    Enums.CommandType CommandType);
