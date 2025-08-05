namespace SmartVoiceAgent.Core.Models;
public class OpenRouterResponse
{
    public string Id { get; set; } = "";
    public List<OpenRouterChoice> Choices { get; set; } = new();
}

public class OpenRouterChoice
{
    public string Text { get; set; } = "";
    public int Index { get; set; }
    public string FinishReason { get; set; } = "";
}
public class ChatCompletionResponse
{
    public string Id { get; set; } = "";
    public List<ChatChoice> Choices { get; set; } = new();
}

public class ChatChoice
{
    public ChatMessage Message { get; set; } = new();
    public int Index { get; set; }
    public string FinishReason { get; set; } = "";
}

public class ChatMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}