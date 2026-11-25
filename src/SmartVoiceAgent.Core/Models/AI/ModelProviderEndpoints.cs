namespace SmartVoiceAgent.Core.Models.AI;

/// <summary>
/// Fixed endpoints and key pages of providers that have a single public API.
/// </summary>
public static class ModelProviderEndpoints
{
    /// <summary>The DeepSeek OpenAI-compatible API.</summary>
    public const string DeepSeek = "https://api.deepseek.com/v1";

    /// <summary>The page where a DeepSeek API key is created.</summary>
    public const string DeepSeekKeyPage = "https://platform.deepseek.com/api_keys";
}
