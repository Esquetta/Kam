using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartVoiceAgent.Core.Models.AI;

namespace SmartVoiceAgent.Ui.Services;

public sealed class ModelsDevModelCatalogService : IModelCatalogService, IDisposable
{
    private static readonly Uri ModelsDevApiUri = new("https://models.dev/api.json");

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public ModelsDevModelCatalogService()
        : this(new HttpClient(), ownsHttpClient: true)
    {
    }

    public ModelsDevModelCatalogService(HttpClient httpClient)
        : this(httpClient, ownsHttpClient: false)
    {
    }

    private ModelsDevModelCatalogService(HttpClient httpClient, bool ownsHttpClient)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<IReadOnlyList<ModelCatalogEntry>> GetModelsAsync(
        ModelProviderProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        using var request = new HttpRequestMessage(HttpMethod.Get, ModelsDevApiUri);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Models.dev catalog request failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var checkedAt = DateTimeOffset.UtcNow;
        var entries = new List<ModelCatalogEntry>();
        foreach (var providerProperty in document.RootElement.EnumerateObject())
        {
            if (!ShouldIncludeProvider(profile.Provider, providerProperty.Name))
            {
                continue;
            }

            entries.AddRange(ParseProviderModels(profile.Provider, providerProperty.Value, checkedAt));
        }

        return entries
            .Where(model => model.Capabilities.Count == 0
                || model.Capabilities.Contains("text-output", StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(model => model.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static bool ShouldIncludeProvider(ModelProviderType requestedProvider, string providerId)
    {
        return requestedProvider switch
        {
            ModelProviderType.OpenAI => providerId.Equals("openai", StringComparison.OrdinalIgnoreCase),
            ModelProviderType.OpenRouter => true,
            _ => false
        };
    }

    private static IEnumerable<ModelCatalogEntry> ParseProviderModels(
        ModelProviderType requestedProvider,
        JsonElement provider,
        DateTimeOffset checkedAt)
    {
        if (!provider.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        var providerId = GetString(provider, "id");
        if (string.IsNullOrWhiteSpace(providerId))
        {
            yield break;
        }

        foreach (var modelProperty in models.EnumerateObject())
        {
            if (modelProperty.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var entry = CreateEntry(requestedProvider, providerId, modelProperty.Value, checkedAt);
            if (!string.IsNullOrWhiteSpace(entry.ModelId))
            {
                yield return entry;
            }
        }
    }

    private static ModelCatalogEntry CreateEntry(
        ModelProviderType requestedProvider,
        string providerId,
        JsonElement model,
        DateTimeOffset checkedAt)
    {
        var modelId = GetString(model, "id");
        var catalogModelId = requestedProvider == ModelProviderType.OpenRouter
            ? $"{providerId}/{modelId}"
            : modelId;

        return new ModelCatalogEntry
        {
            Provider = requestedProvider,
            ProviderId = providerId,
            ModelId = catalogModelId,
            DisplayName = GetString(model, "name", modelId),
            Source = "models.dev",
            ContextWindow = GetNestedInt32(model, "limit", "context"),
            MaxOutputTokens = GetNestedInt32(model, "limit", "output"),
            InputPricePerMillionTokens = GetNestedDecimal(model, "cost", "input"),
            OutputPricePerMillionTokens = GetNestedDecimal(model, "cost", "output"),
            Capabilities = GetCapabilities(model),
            IsAvailable = false,
            LastCheckedAt = checkedAt
        };
    }

    private static IReadOnlyList<string> GetCapabilities(JsonElement model)
    {
        var capabilities = new List<string>();

        AddBooleanCapability(capabilities, model, "reasoning", "reasoning");
        AddBooleanCapability(capabilities, model, "tool_call", "tool-calling");
        AddBooleanCapability(capabilities, model, "structured_output", "structured-output");

        if (TryGetNestedProperty(model, "modalities", "input", out var inputModalities))
        {
            AddModalities(capabilities, inputModalities, "input");
        }

        if (TryGetNestedProperty(model, "modalities", "output", out var outputModalities))
        {
            AddModalities(capabilities, outputModalities, "output");
        }

        return capabilities
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddBooleanCapability(
        ICollection<string> capabilities,
        JsonElement model,
        string propertyName,
        string capability)
    {
        if (model.TryGetProperty(propertyName, out var value)
            && value.ValueKind is JsonValueKind.True)
        {
            capabilities.Add(capability);
        }
    }

    private static void AddModalities(ICollection<string> capabilities, JsonElement modalities, string direction)
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

    private static string GetString(JsonElement item, string propertyName, string fallback = "")
    {
        return item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
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

    private static decimal? GetNestedDecimal(JsonElement item, string parentName, string propertyName)
    {
        if (!TryGetNestedProperty(item, parentName, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
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
