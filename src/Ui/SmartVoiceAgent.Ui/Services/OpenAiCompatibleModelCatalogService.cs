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

    public async Task<IReadOnlyList<string>> GetModelIdsAsync(
        ModelProviderProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrWhiteSpace(profile.Endpoint)
            || !Uri.TryCreate(profile.Endpoint, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException("A valid model provider endpoint is required.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, CreateModelsUri(endpoint));
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

        var allModelIds = data
            .EnumerateArray()
            .Select(item => item.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var textModelIds = allModelIds
            .Where(IsLikelyTextGenerationModel)
            .OrderByDescending(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return textModelIds.Length > 0
            ? textModelIds
            : allModelIds.OrderByDescending(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
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
        return endpointText.EndsWith("/models", StringComparison.OrdinalIgnoreCase)
            ? new Uri(endpointText)
            : new Uri($"{endpointText}/models");
    }

    private static bool IsLikelyTextGenerationModel(string modelId)
    {
        var id = modelId.ToLowerInvariant();
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

        return !excludedFragments.Any(id.Contains);
    }
}
