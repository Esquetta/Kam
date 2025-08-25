using System.Text.Json.Serialization;

namespace SmartVoiceAgent.Core.Models;

public class OpenRouterResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("object")]
    public string Object { get; set; } = "";

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("choices")]
    public List<OpenRouterChoice> Choices { get; set; } = new();

    [JsonPropertyName("usage")]
    public OpenRouterUsage? Usage { get; set; }
}

public class OpenRouterChoice
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("logprobs")]
    public object? Logprobs { get; set; }

    [JsonPropertyName("finish_reason")]
    public string FinishReason { get; set; } = "";
}

public class OpenRouterUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

public class ChatCompletionResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("object")]
    public string Object { get; set; } = "";

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("choices")]
    public List<ChatCompletionChoice> Choices { get; set; } = new();

    [JsonPropertyName("usage")]
    public OpenRouterUsage? Usage { get; set; }
}

public class ChatCompletionChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public ChatMessage? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string FinishReason { get; set; } = "";
}

public class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}