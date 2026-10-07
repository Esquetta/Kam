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
                "gpt-6.1-sol",
                "gpt-6-sol",
                "gpt-6-luna",
                "gpt-6-astra",
                "gpt-5.5",
                "gpt-5.4-mini",
                "gpt-5.4-nano"
            ],
            ModelProviderType.Anthropic =>
            [
                "claude-opus-5-5",
                "claude-sonnet-5-5",
                "claude-fable-5-1",
                "claude-haiku-4-5-20251001"
            ],
            ModelProviderType.OpenRouter =>
            [
                "openai/gpt-6.1-sol",
                "openai/gpt-6-sol",
                "openai/gpt-5.5",
                "openai/gpt-5.4-mini",
                "anthropic/claude-opus-5.5",
                "anthropic/claude-sonnet-5.5",
                "anthropic/claude-haiku-4.5",
                "google/gemini-3.8-flash"
            ],
            ModelProviderType.Ollama =>
            [
                "qwen3",
                "gpt-oss",
                "llama4",
                "gemma3",
                "qwen3-coder",
                "deepseek-r1"
            ],
            _ =>
            [
                "openai/gpt-6.1-sol",
                "openai/gpt-5.5",
                "openai/gpt-5.4-mini",
                "anthropic/claude-opus-5.5",
                "anthropic/claude-sonnet-5.5",
                "anthropic/claude-haiku-4.5",
                "google/gemini-3.8-flash"
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
