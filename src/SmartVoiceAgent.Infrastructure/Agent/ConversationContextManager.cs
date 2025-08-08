using SmartVoiceAgent.Core.Models;
using System.Collections.Concurrent;

/// <summary>
/// Conversation context manager with memory and state tracking
/// </summary>
public class ConversationContextManager
{
    private readonly ConcurrentDictionary<string, ConversationContext> _contexts = new();
    private readonly ConcurrentDictionary<string, ApplicationState> _applicationStates = new();
    private readonly ConcurrentDictionary<string, UserPreference> _userPreferences = new();
    private readonly Queue<ConversationHistory> _conversationHistory = new();
    private readonly object _historyLock = new();

    /// <summary>
    /// Starts a new conversation context
    /// </summary>
    public void StartConversation(string conversationId, string initialMessage)
    {
        var context = new ConversationContext
        {
            ConversationId = conversationId,
            StartTime = DateTime.UtcNow,
            InitialMessage = initialMessage,
            Messages = new List<ContextMessage>()
        };

        _contexts.TryAdd(conversationId, context);

        Console.WriteLine($"🔄 Context started for: {conversationId}");
    }

    /// <summary>
    /// Gets relevant context for processing a command
    /// </summary>
    public string GetRelevantContext(string userInput)
    {
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
            while (_conversationHistory.Count > 50)
            {
                _conversationHistory.Dequeue();
            }
        }

        // Learn from user patterns
        LearnUserPreferences(input, result);

        Console.WriteLine($"📝 Context updated: {type}");
    }

    /// <summary>
    /// Checks if application is currently open
    /// </summary>
    public bool IsApplicationOpen(string appName)
    {
        return _applicationStates.TryGetValue(appName.ToLower(), out var state) && state.IsOpen;
    }

    /// <summary>
    /// Sets application state
    /// </summary>
    public void SetApplicationState(string appName, bool isOpen)
    {
        var key = appName.ToLower();
        _applicationStates.AddOrUpdate(key,
            new ApplicationState { ApplicationName = appName, IsOpen = isOpen, LastUsed = DateTime.UtcNow },
            (k, existing) =>
            {
                existing.IsOpen = isOpen;
                existing.LastUsed = DateTime.UtcNow;
                return existing;
            });

        Console.WriteLine($"📱 App state: {appName} = {(isOpen ? "Open" : "Closed")}");
    }

    /// <summary>
    /// Ends conversation and cleans up context
    /// </summary>
    public void EndConversation(string conversationId)
    {
        if (_contexts.TryRemove(conversationId, out var context))
        {
            context.EndTime = DateTime.UtcNow;
            Console.WriteLine($"✅ Context ended for: {conversationId} (Duration: {context.Duration})");
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
            new UserPreference { Category = category, Value = value, LastUpdated = DateTime.UtcNow },
            (k, existing) =>
            {
                existing.Value = value;
                existing.LastUpdated = DateTime.UtcNow;
                return existing;
            });

        Console.WriteLine($"💡 Learned preference: {category} = {value}");
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
}