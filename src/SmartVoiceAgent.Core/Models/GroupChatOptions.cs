namespace SmartVoiceAgent.Core.Models;
/// <summary>
/// Configuration options for group chat
/// </summary>
public class GroupChatOptions
{
    public bool EnableWebSearchAgent { get; set; } = true;
    public bool EnableAnalyticsAgent { get; set; } = true;
    public bool EnableContextMemory { get; set; } = true;
    public int MaxConversationHistory { get; set; } = 50;
    public TimeSpan ContextRetentionTime { get; set; } = TimeSpan.FromHours(24);
}