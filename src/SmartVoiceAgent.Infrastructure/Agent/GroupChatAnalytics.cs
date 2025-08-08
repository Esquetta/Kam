using SmartVoiceAgent.Core.Models;
using System.Collections.Concurrent;

namespace SmartVoiceAgent.Application.Agent;
/// <summary>
/// Group chat analytics and performance monitoring
/// </summary>
public class GroupChatAnalytics
{
    private readonly ConcurrentQueue<ConversationMetrics> _conversationMetrics = new();
    private readonly ConcurrentDictionary<string, AgentPerformance> _agentPerformance = new();
    private readonly ConcurrentQueue<ErrorMetric> _errorMetrics = new();

    /// <summary>
    /// Records conversation metrics
    /// </summary>
    public void RecordConversation(ConversationMetrics metrics)
    {
        _conversationMetrics.Enqueue(metrics);
        Console.WriteLine($"📊 Recorded conversation: {metrics.ConversationId} - {(metrics.Success ? "✅" : "❌")} ({metrics.Duration.TotalMilliseconds:F0}ms)");

        // Keep only recent metrics
        while (_conversationMetrics.Count > 1000)
        {
            _conversationMetrics.TryDequeue(out _);
        }
    }

    /// <summary>
    /// Records agent performance
    /// </summary>
    public void RecordAgentPerformance(string agentName, TimeSpan responseTime, bool success)
    {
        _agentPerformance.AddOrUpdate(agentName,
            new AgentPerformance
            {
                AgentName = agentName,
                TotalCalls = 1,
                TotalResponseTime = responseTime,
                SuccessfulCalls = success ? 1 : 0
            },
            (key, existing) =>
            {
                existing.TotalCalls++;
                existing.TotalResponseTime += responseTime;
                if (success) existing.SuccessfulCalls++;
                return existing;
            });
    }

    /// <summary>
    /// Records error for analysis
    /// </summary>
    public void RecordError(string component, string errorMessage, string context = null)
    {
        _errorMetrics.Enqueue(new ErrorMetric
        {
            Timestamp = DateTime.UtcNow,
            Component = component,
            ErrorMessage = errorMessage,
            Context = context
        });

        Console.WriteLine($"🚨 Error recorded: {component} - {errorMessage}");

        // Keep only recent errors
        while (_errorMetrics.Count > 500)
        {
            _errorMetrics.TryDequeue(out _);
        }
    }

    /// <summary>
    /// Generates analytics report
    /// </summary>
    public AnalyticsReport GenerateReport(TimeSpan? period = null)
    {
        var cutoff = DateTime.UtcNow - (period ?? TimeSpan.FromHours(24));

        var recentConversations = _conversationMetrics
            .Where(c => c.StartTime >= cutoff)
            .ToList();

        var totalConversations = recentConversations.Count;
        var successfulConversations = recentConversations.Count(c => c.Success);
        var averageResponseTime = recentConversations.Any()
            ? recentConversations.Average(c => c.Duration.TotalMilliseconds)
            : 0;

        var mostUsedCommands = recentConversations
            .GroupBy(c => ExtractCommandType(c.UserInput))
            .OrderByDescending(g => g.Count())
            .Take(5)
            .ToDictionary(g => g.Key, g => g.Count());

        var agentStats = _agentPerformance.Values
            .Select(ap => new AgentStats
            {
                AgentName = ap.AgentName,
                TotalCalls = ap.TotalCalls,
                SuccessRate = ap.TotalCalls > 0 ? (double)ap.SuccessfulCalls / ap.TotalCalls * 100 : 0,
                AverageResponseTime = ap.TotalCalls > 0 ? ap.TotalResponseTime.TotalMilliseconds / ap.TotalCalls : 0
            })
            .ToList();

        var recentErrors = _errorMetrics
            .Where(e => e.Timestamp >= cutoff)
            .GroupBy(e => e.Component)
            .ToDictionary(g => g.Key, g => g.Count());

        return new AnalyticsReport
        {
            Period = period ?? TimeSpan.FromHours(24),
            TotalConversations = totalConversations,
            SuccessRate = totalConversations > 0 ? (double)successfulConversations / totalConversations * 100 : 0,
            AverageResponseTime = averageResponseTime,
            MostUsedCommands = mostUsedCommands,
            AgentStats = agentStats,
            ErrorsByComponent = recentErrors,
            GeneratedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Extracts command type from user input for analytics
    /// </summary>
    private string ExtractCommandType(string userInput)
    {
        if (string.IsNullOrEmpty(userInput)) return "unknown";

        var input = userInput.ToLower();

        if (input.Contains("aç") || input.Contains("open")) return "app_control";
        if (input.Contains("kapat") || input.Contains("close")) return "app_control";
        if (input.Contains("çal") || input.Contains("play")) return "media_control";
        if (input.Contains("görev") || input.Contains("task")) return "task_management";
        if (input.Contains("hatırla") || input.Contains("remind")) return "reminder";
        if (input.Contains("ara") || input.Contains("search")) return "web_search";
        if (input.Contains("bluetooth") || input.Contains("wifi")) return "device_control";

        return "other";
    }

    /// <summary>
    /// Gets real-time performance metrics
    /// </summary>
    public PerformanceMetrics GetCurrentMetrics()
    {
        var recent = _conversationMetrics.TakeLast(10).ToList();

        return new PerformanceMetrics
        {
            RecentConversations = recent.Count,
            AverageResponseTime = recent.Any() ? recent.Average(c => c.Duration.TotalMilliseconds) : 0,
            SuccessRate = recent.Any() ? (double)recent.Count(c => c.Success) / recent.Count * 100 : 0,
            ActiveAgents = _agentPerformance.Count,
            RecentErrors = _errorMetrics.TakeLast(10).Count(),
            Timestamp = DateTime.UtcNow
        };
    }

}
