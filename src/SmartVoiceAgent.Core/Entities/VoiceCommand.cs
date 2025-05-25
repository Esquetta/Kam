namespace SmartVoiceAgent.Core.Entities;

/// <summary>
/// Represents a voice command issued by the user.
/// </summary>
/// <param name="CommandText">The raw command text spoken by the user.</param>
/// <param name="Timestamp">When the command was received.</param>
/// <param name="Confidence">Recognition confidence level (0.0 to 1.0).</param>
public record VoiceCommand(
    string CommandText,
    DateTime Timestamp,
    double Confidence);
