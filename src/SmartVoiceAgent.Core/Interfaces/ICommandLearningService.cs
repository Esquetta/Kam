namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Service interface for learning new commands dynamically.
/// </summary>
public interface ICommandLearningService
{
    /// <summary>
    /// Learns a new command asynchronously.
    /// </summary>
    /// <param name="command">The command to learn.</param>
    /// <returns>True if learning was successful; otherwise false.</returns>
    Task<bool> LearnCommandAsync(string command);
}
