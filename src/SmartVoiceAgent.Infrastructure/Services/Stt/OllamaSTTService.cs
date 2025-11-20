using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Audio;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SmartVoiceAgent.Infrastructure.Services.Stt;
/// <summary>
/// Alternative: Using Ollama local API for STT
/// Requires Ollama running locally with a speech model
/// </summary>
public class OllamaSTTService : ISpeechToTextService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaSTTService> _logger;
    private readonly string _baseUrl;
    private readonly string _modelName;

    public OllamaSTTService(HttpClient httpClient, ILogger<OllamaSTTService> logger, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _baseUrl = configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
        _modelName = configuration["Ollama:STTModel"] ?? "whisper";
    }

    public async Task<SpeechResult> ConvertToTextAsync(byte[] audioData, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Base64 encode audio
            var base64Audio = Convert.ToBase64String(audioData);

            var request = new
            {
                model = _modelName,
                prompt = "Transcribe this audio to text:",
                images = new[] { base64Audio },
                stream = false
            };

            var json =JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<OllamaResponse>(responseJson);

            stopwatch.Stop();

            return new SpeechResult
            {
                Text = result?.Response ?? string.Empty,
                Confidence = 0.8f, // Ollama doesn't provide confidence scores
                ProcessingTime = stopwatch.Elapsed,
                ErrorMessage = string.Empty
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error in Ollama STT conversion");

            return new SpeechResult
            {
                Text = string.Empty,
                Confidence = 0f,
                ProcessingTime = stopwatch.Elapsed,
                ErrorMessage = ex.Message
            };
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private class OllamaResponse
    {
        public string Response { get; set; } = string.Empty;
    }
}