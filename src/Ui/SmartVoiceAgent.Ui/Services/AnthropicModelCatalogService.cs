using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartVoiceAgent.Core.Models.AI;

namespace SmartVoiceAgent.Ui.Services;

public sealed class AnthropicModelCatalogService : IModelCatalogService, IDisposable
{
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public AnthropicModelCatalogService()
        : this(new HttpClient(), ownsHttpClient: true)
    {
    }

    public AnthropicModelCatalogService(HttpClient httpClient)
        : this(httpClient, ownsHttpClient: false)
    {
    }

    private AnthropicModelCatalogService(HttpClient httpClient, bool ownsHttpClient)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<IReadOnlyList<ModelCatalogEntry>> GetModelsAsync(
        ModelProviderProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Provider != ModelProviderType.Anthropic)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(profile.Endpoint)
            || !Uri.TryCreate(profile.Endpoint, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException("A valid Anthropic endpoint is required.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, CreateModelsUri(endpoint));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("anthropic-version", AnthropicVersion);

        if (!string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            request.Headers.Add("x-api-key", profile.ApiKey);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Anthropic model catalog request failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var checkedAt = DateTimeOffset.UtcNow;
        return data
            .EnumerateArray()
            .Select(item => CreateEntry(item, checkedAt))
            .Where(model => !string.IsNullOrWhiteSpace(model.ModelId))
            .DistinctBy(model => model.ModelId, StringComparer.OrdinalIgnoreCase)
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

    private static Uri CreateModelsUri(Uri endpoint)
    {
        var endpointText = endpoint.ToString().TrimEnd('/');
        if (endpointText.EndsWith("/v1/models", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(endpointText);
        }

        if (endpointText.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri($"{endpointText}/models");
        }

        return new Uri($"{endpointText}/v1/models");
    }

    private static ModelCatalogEntry CreateEntry(JsonElement item, DateTimeOffset checkedAt)
    {
        var modelId = GetString(item, "id");

        return new ModelCatalogEntry
        {
            Provider = ModelProviderType.Anthropic,
            ProviderId = "anthropic",
            ModelId = modelId,
            DisplayName = GetString(item, "display_name", modelId),
            Source = "provider-live",
            Capabilities = ["text-input", "text-output"],
            IsAvailable = true,
            LastCheckedAt = checkedAt
        };
    }

    private static string GetString(JsonElement item, string propertyName, string fallback = "")
    {
        return item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }
}
