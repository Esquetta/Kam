using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Audio;
using SmartVoiceAgent.Infrastructure.Services.Voice;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Transcribes speech with an OpenAI-compatible <c>/audio/transcriptions</c> endpoint, such as OpenAI, Groq or
/// a local Whisper server. Endpoint, model and key come from <c>Voice:SpeechApi:*</c> on each call.
/// </summary>
public sealed class OpenAiTranscriptionService : ISpeechToTextService
{
    /// <summary>
    /// The name of the HTTP client this service uses.
    /// </summary>
    public const string HttpClientName = "SpeechApi";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAiTranscriptionService> _logger;

    /// <summary>
    /// Creates the service.
    /// </summary>
    /// <param name="httpClientFactory">Creates the HTTP client.</param>
    /// <param name="configuration">The configuration Settings write to.</param>
    /// <param name="logger">The logger.</param>
    public OpenAiTranscriptionService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<OpenAiTranscriptionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Returns the transcription URL for a base endpoint such as <c>https://api.openai.com/v1</c>, or null when it
    /// isn't an http or https URL.
    /// </summary>
    /// <param name="endpoint">The base endpoint.</param>
    public static Uri? BuildRequestUri(string endpoint)
    {
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        if (!path.EndsWith("/audio/transcriptions", StringComparison.OrdinalIgnoreCase))
        {
            path += "/audio/transcriptions";
        }

        return new UriBuilder(uri) { Path = path, Query = string.Empty }.Uri;
    }

    /// <inheritdoc />
    public async Task<SpeechResult> ConvertToTextAsync(byte[] audioData, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var settings = VoiceSettings.Read(_configuration);
        var requestUri = BuildRequestUri(settings.ApiEndpoint);
        if (requestUri is null)
        {
            return Failed("The transcription endpoint must be an http or https URL.");
        }

        try
        {
            using var content = new MultipartFormDataContent();
            var file = new ByteArrayContent(WaveAudio.ToWave(audioData));
            file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            content.Add(file, "file", "speech.wav");
            content.Add(new StringContent(settings.ApiModel), "model");
            content.Add(new StringContent("json"), "response_format");
            content.Add(new StringContent("0"), "temperature");
            if (settings.Language != VoiceSettings.AutoLanguage)
            {
                content.Add(new StringContent(settings.Language), "language");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = content };
            if (settings.ApiKey is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            }

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Transcription API returned {Status}", (int)response.StatusCode);
                return Failed($"The transcription API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var text = TranscriptCleaner.Clean(ReadText(body));
            return new SpeechResult
            {
                Text = text,
                Confidence = text.Length == 0 ? 0f : 0.9f,
                ProcessingTime = stopwatch.Elapsed,
                ErrorMessage = text.Length == 0 ? TranscriptCleaner.NoSpeechMessage : string.Empty
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Transcription API request failed");
            return Failed(ex.Message);
        }

        SpeechResult Failed(string message) => new() { ProcessingTime = stopwatch.Elapsed, ErrorMessage = message };
    }

    /// <summary>
    /// Reads the transcript from a JSON (<c>{"text": "..."}</c>) or plain text response.
    /// </summary>
    /// <param name="body">The response body.</param>
    public static string ReadText(string body)
    {
        var trimmed = body.Trim();
        if (!trimmed.StartsWith('{'))
        {
            return trimmed;
        }

        using var document = JsonDocument.Parse(trimmed);
        return document.RootElement.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString() ?? string.Empty
            : string.Empty;
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
