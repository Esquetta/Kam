using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartVoiceAgent.Core.Models.AI;

namespace SmartVoiceAgent.Ui.Services;

public sealed class OpenAiCompatibleModelCatalogService : IModelCatalogService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public OpenAiCompatibleModelCatalogService()
        : this(new HttpClient(), ownsHttpClient: true)
    {
    }

    public OpenAiCompatibleModelCatalogService(HttpClient httpClient)
        : this(httpClient, ownsHttpClient: false)
    {
    }

    private OpenAiCompatibleModelCatalogService(HttpClient httpClient, bool ownsHttpClient)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<IReadOnlyList<ModelCatalogEntry>> GetModelsAsync(
        ModelProviderProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrWhiteSpace(profile.Endpoint)
            || !Uri.TryCreate(profile.Endpoint, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException("A valid model provider endpoint is required.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, CreateModelsUri(profile.Provider, endpoint));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (profile.Provider != ModelProviderType.Ollama && !string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKey);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Model catalog request failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var checkedAt = DateTimeOffset.UtcNow;
        var allModels = data
            .EnumerateArray()
            .Select(item => CreateEntry(profile.Provider, item, checkedAt))
            .Where(model => !string.IsNullOrWhiteSpace(model.ModelId))
            .DistinctBy(model => model.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var textModels = allModels
            .Where(IsLikelyTextGenerationModel)
            .OrderByDescending(model => model.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return textModels.Length > 0
            ? textModels
            : allModels.OrderByDescending(model => model.ModelId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static Uri CreateModelsUri(ModelProviderType provider, Uri endpoint)
    {
        var endpointText = endpoint.ToString().TrimEnd('/');
        var modelsUri = endpointText.EndsWith("/models", StringComparison.OrdinalIgnoreCase)
            ? endpointText
            : $"{endpointText}/models";

        if (provider != ModelProviderType.OpenRouter
            || modelsUri.Contains("output_modalities=", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(modelsUri);
        }

        var separator = modelsUri.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return new Uri($"{modelsUri}{separator}output_modalities=text");
    }

    private static ModelCatalogEntry CreateEntry(
        ModelProviderType provider,
        JsonElement item,
        DateTimeOffset checkedAt)
    {
        var modelId = GetString(item, "id");
        var capabilities = GetCapabilities(item);

        return new ModelCatalogEntry
        {
            Provider = provider,
            ProviderId = GetProviderId(provider, modelId),
            ModelId = modelId,
            DisplayName = GetString(item, "name", modelId),
            Source = "provider-live",
            ContextWindow = GetInt32(item, "context_length")
                ?? GetNestedInt32(item, "top_provider", "context_length"),
            MaxOutputTokens = GetNestedInt32(item, "top_provider", "max_completion_tokens"),
            InputPricePerMillionTokens = GetNestedDecimal(item, "pricing", "prompt", multiplyByMillion: true),
            OutputPricePerMillionTokens = GetNestedDecimal(item, "pricing", "completion", multiplyByMillion: true),
            Capabilities = capabilities,
            IsAvailable = true,
            LastCheckedAt = checkedAt
        };
    }

    private static bool IsLikelyTextGenerationModel(ModelCatalogEntry model)
    {
        var id = model.ModelId.ToLowerInvariant();
        var excludedFragments = new[]
        {
            "audio",
            "babbage",
            "dall-e",
            "davinci",
            "embedding",
            "image",
            "moderation",
            "realtime",
            "sora",
            "transcribe",
            "tts",
            "whisper"
        };

        return !excludedFragments.Any(id.Contains)
            && (model.Capabilities.Count == 0
                || model.Capabilities.Contains("text-output", StringComparer.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> GetCapabilities(JsonElement item)
    {
        var capabilities = new List<string>();

        AddModalities(capabilities, item, "input_modalities", "input");
        AddModalities(capabilities, item, "output_modalities", "output");

        if (TryGetNestedProperty(item, "architecture", "input_modalities", out var architectureInput))
        {
            AddModalities(capabilities, architectureInput, "input");
        }

        if (TryGetNestedProperty(item, "architecture", "output_modalities", out var architectureOutput))
        {
            AddModalities(capabilities, architectureOutput, "output");
        }

        if (item.TryGetProperty("supported_parameters", out var supportedParameters)
            && supportedParameters.ValueKind == JsonValueKind.Array)
        {
            var parameters = supportedParameters
                .EnumerateArray()
                .Select(parameter => parameter.GetString())
                .Where(parameter => !string.IsNullOrWhiteSpace(parameter))
                .Select(parameter => parameter!)
                .ToArray();

            if (parameters.Any(parameter =>
                    parameter.Contains("tool", StringComparison.OrdinalIgnoreCase)
                    || parameter.Contains("function", StringComparison.OrdinalIgnoreCase)))
            {
                capabilities.Add("tool-calling");
            }

            if (parameters.Any(parameter =>
                    parameter.Contains("response_format", StringComparison.OrdinalIgnoreCase)
                    || parameter.Contains("structured", StringComparison.OrdinalIgnoreCase)))
            {
                capabilities.Add("structured-output");
            }
        }

        return capabilities
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddModalities(List<string> capabilities, JsonElement item, string propertyName, string direction)
    {
        if (item.TryGetProperty(propertyName, out var modalities))
        {
            AddModalities(capabilities, modalities, direction);
        }
    }

    private static void AddModalities(List<string> capabilities, JsonElement modalities, string direction)
    {
        if (modalities.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var modality in modalities.EnumerateArray())
        {
            var value = modality.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                capabilities.Add($"{value.ToLowerInvariant()}-{direction}");
            }
        }
    }

    private static string GetProviderId(ModelProviderType provider, string modelId)
    {
        if (provider == ModelProviderType.OpenRouter
            && modelId.Contains('/', StringComparison.Ordinal))
        {
            return modelId.Split('/')[0];
        }

        return provider switch
        {
            ModelProviderType.OpenAI => "openai",
            ModelProviderType.OpenRouter => "openrouter",
            ModelProviderType.Ollama => "ollama",
            _ => "openai-compatible"
        };
    }

    private static string GetString(JsonElement item, string propertyName, string fallback = "")
    {
        return item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static int? GetInt32(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    private static int? GetNestedInt32(JsonElement item, string parentName, string propertyName)
    {
        return TryGetNestedProperty(item, parentName, propertyName, out var value)
            ? GetInt32Value(value)
            : null;
    }

    private static int? GetInt32Value(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    private static decimal? GetNestedDecimal(
        JsonElement item,
        string parentName,
        string propertyName,
        bool multiplyByMillion)
    {
        if (!TryGetNestedProperty(item, parentName, propertyName, out var value))
        {
            return null;
        }

        decimal? number = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var parsed) => parsed,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };

        return number is null
            ? null
            : multiplyByMillion ? number * 1_000_000m : number;
    }

    private static bool TryGetNestedProperty(
        JsonElement item,
        string parentName,
        string propertyName,
        out JsonElement value)
    {
        value = default;
        return item.TryGetProperty(parentName, out var parent)
            && parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(propertyName, out value);
    }
}
