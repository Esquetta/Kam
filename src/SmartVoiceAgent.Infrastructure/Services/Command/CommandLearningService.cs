using Core.CrossCuttingConcerns.Logging.Serilog;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Interfaces;
using System.Collections.Concurrent;
using System.Text.Json;

namespace SmartVoiceAgent.Infrastructure.Services.Command;
/// <summary>
/// Concrete implementation of ICommandLearningService for learning and storing commands.
/// </summary>
public class CommandLearningService : ICommandLearningService
{
    private readonly LoggerServiceBase _logger;
    private readonly ConcurrentDictionary<Guid, LearnedCommand> _commandCache;
    private readonly string _commandStoragePath;
    private readonly SemaphoreSlim _fileSemaphore;
    private readonly JsonSerializerOptions _jsonOptions;

    public CommandLearningService(LoggerServiceBase logger, string storagePath = "learned_commands.json")
    {
        _logger = logger;
        _commandCache = new ConcurrentDictionary<Guid, LearnedCommand>();
        _commandStoragePath = storagePath;
        _fileSemaphore = new SemaphoreSlim(1, 1);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        // Load existing commands on startup
        _ = LoadCommandsAsync();
    }

    /// <summary>
    /// Learns a new command asynchronously.
    /// </summary>
    /// <param name="learnedCommand">The command to learn.</param>
    /// <returns>True if learning was successful; otherwise false.</returns>
    public async Task<bool> LearnCommandAsync(LearnedCommand learnedCommand)
    {
        try
        {
            if (learnedCommand == null)
            {
                _logger.Warn("Attempted to learn null command");
                return false;
            }

            if (string.IsNullOrWhiteSpace(learnedCommand.CommandText))
            {
                _logger.Warn("Attempted to learn command with empty CommandText");
                return false;
            }

            // Validate command
            if (!ValidateCommand(learnedCommand))
            {
                _logger.Warn($"Command validation failed for: {learnedCommand.CommandText}");
                return false;
            }

            // Add or update command in cache
            _commandCache.AddOrUpdate(learnedCommand.Id, learnedCommand, (key, oldValue) => learnedCommand);

            // Persist to storage
            await SaveCommandsAsync();

            _logger.Info($"Successfully learned command: {learnedCommand.CommandText}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error learning command: {learnedCommand?.CommandText}");
            return false;
        }
    }

    /// <summary>
    /// Gets a learned command by ID.
    /// </summary>
    /// <param name="commandId">The ID of the command.</param>
    /// <returns>The learned command if found; otherwise null.</returns>
    public LearnedCommand? GetCommand(Guid commandId)
    {
        return _commandCache.TryGetValue(commandId, out var command) ? command : null;
    }

    /// <summary>
    /// Gets learned commands by text pattern.
    /// </summary>
    /// <param name="commandText">The command text to search for.</param>
    /// <returns>Collection of matching commands.</returns>
    public IEnumerable<LearnedCommand> GetCommandsByText(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
            return Enumerable.Empty<LearnedCommand>();

        return _commandCache.Values
            .Where(cmd => cmd.CommandText.Contains(commandText, StringComparison.OrdinalIgnoreCase))
            .Where(cmd => cmd.IsActive)
            .OrderByDescending(cmd => cmd.Priority)
            .ThenByDescending(cmd => cmd.UsageCount);
    }

    /// <summary>
    /// Gets all learned commands.
    /// </summary>
    /// <returns>Collection of all learned commands.</returns>
    public IEnumerable<LearnedCommand> GetAllCommands()
    {
        return _commandCache.Values
            .Where(cmd => cmd.IsActive)
            .OrderByDescending(cmd => cmd.Priority)
            .ThenByDescending(cmd => cmd.UsageCount)
            .ToList();
    }

    /// <summary>
    /// Removes a learned command.
    /// </summary>
    /// <param name="commandId">The ID of the command to remove.</param>
    /// <returns>True if command was removed; otherwise false.</returns>
    public async Task<bool> RemoveCommandAsync(Guid commandId)
    {
        try
        {
            if (_commandCache.TryRemove(commandId, out var removedCommand))
            {
                await SaveCommandsAsync();
                _logger.Info($"Successfully removed command: {removedCommand.CommandText}");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error removing command: {commandId}");
            return false;
        }
    }

    /// <summary>
    /// Updates command usage statistics.
    /// </summary>
    /// <param name="commandId">The ID of the command that was used.</param>
    /// <returns>True if updated successfully; otherwise false.</returns>
    public async Task<bool> UpdateUsageAsync(Guid commandId)
    {
        try
        {
            if (_commandCache.TryGetValue(commandId, out var command))
            {
                var updatedCommand = command with
                {
                    UsageCount = command.UsageCount + 1,
                    LastUsedAt = DateTime.UtcNow
                };

                _commandCache.TryUpdate(commandId, updatedCommand, command);
                await SaveCommandsAsync();
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error updating usage for command: {commandId}");
            return false;
        }
    }

    /// <summary>
    /// Gets commands by category.
    /// </summary>
    /// <param name="category">The category to filter by.</param>
    /// <returns>Collection of commands in the specified category.</returns>
    public IEnumerable<LearnedCommand> GetCommandsByCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return Enumerable.Empty<LearnedCommand>();

        return _commandCache.Values
            .Where(cmd => string.Equals(cmd.Category, category, StringComparison.OrdinalIgnoreCase))
            .Where(cmd => cmd.IsActive)
            .OrderByDescending(cmd => cmd.Priority)
            .ThenByDescending(cmd => cmd.UsageCount);
    }

    /// <summary>
    /// Gets the most used commands.
    /// </summary>
    /// <param name="count">Number of commands to return.</param>
    /// <returns>Collection of most used commands.</returns>
    public IEnumerable<LearnedCommand> GetMostUsedCommands(int count = 10)
    {
        return _commandCache.Values
            .Where(cmd => cmd.IsActive)
            .OrderByDescending(cmd => cmd.UsageCount)
            .Take(count);
    }

    /// <summary>
    /// Validates a command before learning.
    /// </summary>
    /// <param name="command">The command to validate.</param>
    /// <returns>True if valid; otherwise false.</returns>
    private bool ValidateCommand(LearnedCommand command)
    {
        if (command == null)
            return false;

        if (string.IsNullOrWhiteSpace(command.CommandText))
            return false;

        if (command.Id == Guid.Empty)
            return false;

        // Add additional validation rules as needed
        // e.g., check for reserved keywords, command format, etc.

        return true;
    }

    /// <summary>
    /// Loads commands from persistent storage.
    /// </summary>
    private async Task LoadCommandsAsync()
    {
        try
        {
            await _fileSemaphore.WaitAsync();

            if (!File.Exists(_commandStoragePath))
            {
                _logger.Info("No existing command storage found. Starting with empty command set.");
                return;
            }

            var json = await File.ReadAllTextAsync(_commandStoragePath);
            var commands = JsonSerializer.Deserialize<List<LearnedCommand>>(json, _jsonOptions);

            if (commands != null)
            {
                _commandCache.Clear();
                foreach (var command in commands)
                {
                    if (ValidateCommand(command))
                    {
                        _commandCache.TryAdd(command.Id, command);
                    }
                }

                _logger.Info($"Loaded {commands.Count} commands from storage");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Error loading commands from storage");
        }
        finally
        {
            _fileSemaphore.Release();
        }
    }

    /// <summary>
    /// Saves commands to persistent storage.
    /// </summary>
    private async Task SaveCommandsAsync()
    {
        try
        {
            await _fileSemaphore.WaitAsync();

            var commands = _commandCache.Values.ToList();
            var json = JsonSerializer.Serialize(commands, _jsonOptions);

            await File.WriteAllTextAsync(_commandStoragePath, json);
            _logger.Debug($"Saved {commands.Count} commands to storage");
        }
        catch (Exception ex)
        {
            _logger.Error("Error saving commands to storage");
            throw;
        }
        finally
        {
            _fileSemaphore.Release();
        }
    }

    /// <summary>
    /// Clears all learned commands.
    /// </summary>
    public async Task ClearAllCommandsAsync()
    {
        try
        {
            _commandCache.Clear();
            await SaveCommandsAsync();
            _logger.Info("Cleared all learned commands");
        }
        catch (Exception ex)
        {
            _logger.Error($"Error clearing commands: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    public void Dispose()
    {
        _fileSemaphore?.Dispose();
    }
}