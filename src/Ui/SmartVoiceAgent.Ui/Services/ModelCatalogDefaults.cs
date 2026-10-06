using System;
using System.Collections.Generic;
using System.Linq;
using SmartVoiceAgent.Core.Models.AI;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// The short list of models offered for each provider before a live model list is loaded.
/// Settings and the chat model picker share it.
/// </summary>
public static class ModelCatalogDefaults
{
    /// <summary>
    /// Returns the suggested models for a provider, with the current model first when it is not among them.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <param name="currentModel">The model in use, which is always included.</param>
    public static IReadOnlyList<string> GetModelIds(ModelProviderType provider, string? currentModel)
    {
        IEnumerable<string> defaults = provider switch
        {
            ModelProviderType.OpenAI =>
            [
                "gpt-5.5",
                "gpt-5.4",
                "gpt-5.4-mini",
                "gpt-5.4-nano",
                "gpt-4.1",
                "gpt-4.1-mini",
                "gpt-4o",
                "gpt-4o-mini"
            ],
            ModelProviderType.Anthropic =>
            [
                "claude-opus-4-7",
                "claude-sonnet-4-6",
                "claude-haiku-4-5-20251001",
                "claude-opus-4-6",
                "claude-sonnet-4-5-20250929"
            ],
            ModelProviderType.OpenRouter =>
            [
                "openai/gpt-5.5",
                "openai/gpt-5.4",
                "openai/gpt-5.4-mini",
                "openai/gpt-5.4-nano",
                "openai/gpt-4.1-mini",
                "openai/gpt-4o-mini",
                "anthropic/claude-opus-4-7",
                "anthropic/claude-sonnet-4-6",
                "anthropic/claude-haiku-4-5-20251001",
                "google/gemini-2.0-flash-001"
            ],
            ModelProviderType.Ollama =>
            [
                "llama3.1",
                "llama3.2",
                "mistral",
                "qwen2.5-coder"
            ],
            _ =>
            [
                "openai/gpt-5.5",
                "openai/gpt-5.4-mini",
                "openai/gpt-4o-mini",
                "anthropic/claude-opus-4-7",
                "anthropic/claude-sonnet-4-6",
                "anthropic/claude-haiku-4-5-20251001",
                "google/gemini-2.0-flash-001"
            ]
        };

        var options = defaults
            .Where(modelId => !string.IsNullOrWhiteSpace(modelId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (string.IsNullOrWhiteSpace(currentModel)
            || options.Contains(currentModel, StringComparer.OrdinalIgnoreCase))
        {
            return options;
        }

        return options.Prepend(currentModel).ToArray();
    }
}
