using SmartVoiceAgent.Core.Models;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

/// <summary>
/// Conversation context manager with memory and state tracking
/// Includes automatic cleanup to prevent memory leaks
/// </summary>
public class ConversationContextManager : IDisposable
{
    private readonly ConcurrentDictionary<string, ConversationContext> _contexts = new();
    private readonly ConcurrentDictionary<string, ApplicationState> _applicationStates = new();
    private readonly ConcurrentDictionary<string, UserPreference> _userPreferences = new();
    private readonly Queue<ConversationHistory> _conversationHistory = new();
    private readonly object _historyLock = new();
    private readonly Timer? _cleanupTimer;
    private readonly ILogger<ConversationContextManager>? _logger;
    private bool _disposed;

    // Configuration limits
    private const int MaxHistoryItems = 100;
    private const int MaxContextAgeHours = 24;
    private const int MaxAppStateAgeHours = 12;
    private const int CleanupIntervalMinutes = 30;

    public ConversationContextManager(ILogger<ConversationContextManager>? logger = null)
    {
        _logger = logger;
        
        // Setup periodic cleanup
        _cleanupTimer = new Timer(
            _ => CleanupOldData(),
            null,
            TimeSpan.FromMinutes(CleanupIntervalMinutes),
            TimeSpan.FromMinutes(CleanupIntervalMinutes));
    }

    /// <summary>
    /// Starts a new conversation context
    /// </summary>
    public void StartConversation(string conversationId, string initialMessage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var context = new ConversationContext
        {
            ConversationId = conversationId,
            StartTime = DateTime.UtcNow,
            InitialMessage = initialMessage,
            Messages = new List<ContextMessage>()
        };

        _contexts.TryAdd(conversationId, context);
        _logger?.LogDebug("Context started for: {ConversationId}", conversationId);
    }

    /// <summary>
    /// Gets relevant context for processing a command
    /// </summary>
    public string GetRelevantContext(string userInput)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var contextInfo = new List<string>();

        // Add application states
        var openApps = _applicationStates.Values
            .Where(app => app.IsOpen)
            .Select(app => app.ApplicationName)
            .ToList();

        if (openApps.Any())
        {
            contextInfo.Add($"Açık uygulamalar: {string.Join(", ", openApps)}");
        }

        // Add user preferences
        var preferences = GetUserPreferences(userInput);
        if (preferences.Any())
        {
            contextInfo.Add($"Kullanıcı tercihleri: {string.Join(", ", preferences)}");
        }

        // Add recent history
        var recentHistory = GetRecentHistory(userInput);
        if (recentHistory.Any())
        {
            contextInfo.Add($"Son işlemler: {string.Join(", ", recentHistory)}");
        }

        return string.Join(" | ", contextInfo);
    }

    /// <summary>
    /// Updates context with new information
    /// </summary>
    public void UpdateContext(string type, string input, string result)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_historyLock)
        {
            _conversationHistory.Enqueue(new ConversationHistory
            {
                Timestamp = DateTime.UtcNow,
                Type = type,
                Input = input,
                Result = result
            });

            // Keep only recent history
            while (_conversationHistory.Count > MaxHistoryItems)
            {
                _conversationHistory.Dequeue();
            }
        }

        // Learn from user patterns
        LearnUserPreferences(input, result);

        _logger?.LogDebug("Context updated: {Type}", type);
    }

    /// <summary>
    /// Checks if application is currently open
    /// </summary>
    public bool IsApplicationOpen(string appName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _applicationStates.TryGetValue(appName.ToLower(), out var state) && state.IsOpen;
    }

    /// <summary>
    /// Sets application state
    /// </summary>
    public void SetApplicationState(string appName, bool isOpen)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var key = appName.ToLower();
        _applicationStates.AddOrUpdate(key,
            new ApplicationState 
            { 
                ApplicationName = appName, 
                IsOpen = isOpen, 
                LastUsed = DateTime.UtcNow 
            },
            (k, existing) =>
            {
                existing.IsOpen = isOpen;
                existing.LastUsed = DateTime.UtcNow;
                return existing;
            });

        _logger?.LogDebug("App state: {AppName} = {State}", appName, isOpen ? "Open" : "Closed");
    }

    /// <summary>
    /// Ends conversation and cleans up context
    /// </summary>
    public void EndConversation(string conversationId)
    {
        if (_contexts.TryRemove(conversationId, out var context))
        {
            context.EndTime = DateTime.UtcNow;
            _logger?.LogDebug("Context ended for: {ConversationId} (Duration: {Duration})", 
                conversationId, context.Duration);
        }
    }

    /// <summary>
    /// Cleans up old data to prevent memory leaks
    /// </summary>
    public void CleanupOldData()
    {
        try
        {
            var now = DateTime.UtcNow;
            var removedContexts = 0;
            var removedAppStates = 0;
            var removedPreferences = 0;

            // Clean up old contexts
            foreach (var key in _contexts.Keys)
            {
                if (_contexts.TryGetValue(key, out var context))
                {
                    var age = now - context.StartTime;
                    if (age.TotalHours > MaxContextAgeHours || 
                        (context.EndTime.HasValue && context.EndTime.Value < now.AddHours(-1)))
                    {
                        if (_contexts.TryRemove(key, out _))
                            removedContexts++;
                    }
                }
            }

            // Clean up old application states
            foreach (var key in _applicationStates.Keys)
            {
                if (_applicationStates.TryGetValue(key, out var state))
                {
                    var age = now - state.LastUsed;
                    if (age.TotalHours > MaxAppStateAgeHours)
                    {
                        if (_applicationStates.TryRemove(key, out _))
                            removedAppStates++;
                    }
                }
            }

            // Clean up old user preferences (keep even if old, they're small)
            // But remove if older than 30 days
            var preferenceCutoff = now.AddDays(-30);
            foreach (var key in _userPreferences.Keys)
            {
                if (_userPreferences.TryGetValue(key, out var pref))
                {
                    if (pref.LastUpdated < preferenceCutoff)
                    {
                        if (_userPreferences.TryRemove(key, out _))
                            removedPreferences++;
                    }
                }
            }

            if (removedContexts > 0 || removedAppStates > 0 || removedPreferences > 0)
            {
                _logger?.LogInformation(
                    "Cleanup completed: {Contexts} contexts, {AppStates} app states, {Preferences} preferences removed",
                    removedContexts, removedAppStates, removedPreferences);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during cleanup");
        }
    }

    /// <summary>
    /// Gets user preferences based on input pattern
    /// </summary>
    private List<string> GetUserPreferences(string input)
    {
        var preferences = new List<string>();

        foreach (var pref in _userPreferences.Values)
        {
            if (input.ToLower().Contains(pref.Category.ToLower()))
            {
                preferences.Add($"{pref.Category}: {pref.Value}");
            }
        }

        return preferences;
    }

    /// <summary>
    /// Gets recent relevant history
    /// </summary>
    private List<string> GetRecentHistory(string input)
    {
        lock (_historyLock)
        {
            return _conversationHistory
                .TakeLast(10)
                .Where(h => IsRelevantHistory(h, input))
                .Select(h => $"{h.Type}: {h.Input}")
                .ToList();
        }
    }

    /// <summary>
    /// Determines if history item is relevant to current input
    /// </summary>
    private bool IsRelevantHistory(ConversationHistory history, string currentInput)
    {
        var historyWords = history.Input.ToLower().Split(' ');
        var currentWords = currentInput.ToLower().Split(' ');

        return historyWords.Intersect(currentWords).Any();
    }

    /// <summary>
    /// Learns user preferences from interactions
    /// </summary>
    private void LearnUserPreferences(string input, string result)
    {
        // Learn volume preferences
        if (input.ToLower().Contains("ses") || input.ToLower().Contains("volume"))
        {
            if (TryExtractVolumeLevel(result, out var level))
            {
                UpdateUserPreference("volume", level.ToString());
            }
        }

        // Learn application preferences
        if (input.ToLower().Contains("müzik") || input.ToLower().Contains("music"))
        {
            var apps = new[] { "spotify", "youtube", "apple music", "vlc" };
            foreach (var app in apps)
            {
                if (result.ToLower().Contains(app))
                {
                    UpdateUserPreference("preferred_music_app", app);
                    break;
                }
            }
        }

        // Learn browser preferences
        if (input.ToLower().Contains("web") || input.ToLower().Contains("browser"))
        {
            var browsers = new[] { "chrome", "firefox", "edge", "safari" };
            foreach (var browser in browsers)
            {
                if (result.ToLower().Contains(browser))
                {
                    UpdateUserPreference("preferred_browser", browser);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Extracts volume level from result
    /// </summary>
    private bool TryExtractVolumeLevel(string result, out int level)
    {
        level = 0;
        var match = System.Text.RegularExpressions.Regex.Match(result, @"(\d+)%?");
        if (match.Success && int.TryParse(match.Groups[1].Value, out level))
        {
            return level >= 0 && level <= 100;
        }
        return false;
    }

    /// <summary>
    /// Updates user preference
    /// </summary>
    private void UpdateUserPreference(string category, string value)
    {
        _userPreferences.AddOrUpdate(category,
            new UserPreference 
            { 
                Category = category, 
                Value = value, 
                LastUpdated = DateTime.UtcNow 
            },
            (k, existing) =>
            {
                existing.Value = value;
                existing.LastUpdated = DateTime.UtcNow;
                return existing;
            });

        _logger?.LogDebug("Learned preference: {Category} = {Value}", category, value);
    }

    /// <summary>
    /// Gets analytics data for the context manager
    /// </summary>
    public ContextAnalytics GetAnalytics()
    {
        return new ContextAnalytics
        {
            ActiveApplications = _applicationStates.Values.Where(a => a.IsOpen).Count(),
            UserPreferences = _userPreferences.Count,
            ConversationHistorySize = _conversationHistory.Count,
            ActiveContexts = _contexts.Count
        };
    }

    /// <summary>
    /// Disposes resources and stops cleanup timer
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cleanupTimer?.Dispose();
        
        _logger?.LogInformation("ConversationContextManager disposed");
    }
}
