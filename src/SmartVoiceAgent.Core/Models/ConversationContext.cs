namespace SmartVoiceAgent.Core.Models;
public class ConversationContext
{
    public string ConversationId { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string InitialMessage { get; set; } = "";
    public List<ContextMessage> Messages { get; set; } = new();
    public TimeSpan Duration => (EndTime ?? DateTime.UtcNow) - StartTime;
}
public class ContextMessage
{
    public DateTime Timestamp { get; set; }
    public string From { get; set; } = "";
    public string Content { get; set; } = "";
    public string MessageType { get; set; } = "";
}

public class ApplicationState
{
    public string ApplicationName { get; set; } = "";
    public bool IsOpen { get; set; }
    public DateTime LastUsed { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class UserPreference
{
    public string Category { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTime LastUpdated { get; set; }
    public int UsageCount { get; set; }
}

public class ConversationHistory
{
    public DateTime Timestamp { get; set; }
    public string Type { get; set; } = "";
    public string Input { get; set; } = "";
    public string Result { get; set; } = "";
}

public class ConversationMetrics
{
    public string ConversationId { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool Success { get; set; }
    public string UserInput { get; set; } = "";
    public string? FinalResult { get; set; }
    public string? Error { get; set; }
    public int ParticipantCount { get; set; }
    public int MessageCount { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
}

public class AgentPerformance
{
    public string AgentName { get; set; } = "";
    public int TotalCalls { get; set; }
    public int SuccessfulCalls { get; set; }
    public TimeSpan TotalResponseTime { get; set; }
}

public class ErrorMetric
{
    public DateTime Timestamp { get; set; }
    public string Component { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
    public string? Context { get; set; }
}

public class AnalyticsReport
{
    public TimeSpan Period { get; set; }
    public int TotalConversations { get; set; }
    public double SuccessRate { get; set; }
    public double AverageResponseTime { get; set; }
    public Dictionary<string, int> MostUsedCommands { get; set; } = new();
    public List<AgentStats> AgentStats { get; set; } = new();
    public Dictionary<string, int> ErrorsByComponent { get; set; } = new();
    public DateTime GeneratedAt { get; set; }

    public override string ToString()
    {
        return $"""
            📊 ANALYTICS REPORT ({Period.TotalHours:F0}h period)
            ═══════════════════════════════════════════════
            📈 Conversations: {TotalConversations}
            ✅ Success Rate: {SuccessRate:F1}%
            ⚡ Avg Response: {AverageResponseTime:F0}ms
            
            🔥 Top Commands:
            {string.Join("\n", MostUsedCommands.Take(3).Select(kv => $"   • {kv.Key}: {kv.Value}"))}
            
            🤖 Agent Performance:
            {string.Join("\n", AgentStats.Take(3).Select(a => $"   • {a.AgentName}: {a.SuccessRate:F1}% ({a.AverageResponseTime:F0}ms)"))}
            
            Generated: {GeneratedAt:HH:mm:ss}
            """;
    }
}

public class AgentStats
{
    public string AgentName { get; set; } = "";
    public int TotalCalls { get; set; }
    public double SuccessRate { get; set; }
    public double AverageResponseTime { get; set; }
}

public class PerformanceMetrics
{
    public int RecentConversations { get; set; }
    public double AverageResponseTime { get; set; }
    public double SuccessRate { get; set; }
    public int ActiveAgents { get; set; }
    public int RecentErrors { get; set; }
    public DateTime Timestamp { get; set; }
}

public class ContextAnalytics
{
    public int ActiveApplications { get; set; }
    public int UserPreferences { get; set; }
    public int ConversationHistorySize { get; set; }
    public int ActiveContexts { get; set; }
}