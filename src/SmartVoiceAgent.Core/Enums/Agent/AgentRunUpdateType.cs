namespace SmartVoiceAgent.Core.Enums.Agent;

public enum AgentRunUpdateType
{
    Started,
    ContentDelta,
    ToolCall,
    ToolResult,
    Completed,
    Failed
}