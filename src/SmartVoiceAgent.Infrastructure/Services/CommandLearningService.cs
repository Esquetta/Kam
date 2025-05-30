using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Service for learning new voice commands.
/// </summary>
public class CommandLearningService : ICommandLearningService
{
    private readonly List<string> _learnedCommands = new();

    /// <inheritdoc />
    public Task<bool> LearnCommandAsync(LearnedCommand learnedCommand)
    {
        if (string.IsNullOrWhiteSpace(learnedCommand.CommandText))
            return Task.FromResult(false);

        if (_learnedCommands.Contains(learnedCommand.CommandText))
            return Task.FromResult(false); // Zaten varsa ekleme

        _learnedCommands.Add(learnedCommand.CommandText);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Gets all learned commands.
    /// </summary>
    /// <returns>A list of learned commands.</returns>
    public Task<IEnumerable<string>> GetLearnedCommandsAsync()
    {
        return Task.FromResult<IEnumerable<string>>(_learnedCommands);
    }

    
}
