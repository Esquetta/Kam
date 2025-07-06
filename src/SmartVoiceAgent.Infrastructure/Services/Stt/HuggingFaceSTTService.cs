using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Config;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models;
using System.Diagnostics;
using System.Text.Json;

public class HuggingFaceSTTService : ISpeechToTextService
{
    private readonly ILogger<HuggingFaceSTTService> _logger;
    private readonly HuggingFaceConfig _config;
    private readonly SemaphoreSlim _semaphore;
    private readonly HttpClient _httpClient;

    public HuggingFaceSTTService(
        ILogger<HuggingFaceSTTService> logger,
        HuggingFaceConfig config,
        HttpClient httpClient)
    {
        _logger = logger;
        _config = config;
        _semaphore = new SemaphoreSlim(config.MaxConcurrentRequests, config.MaxConcurrentRequests);
        _httpClient = httpClient;

        // Set Hugging Face API key
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);
        }
    }

    public async Task<SpeechResult> ConvertToTextAsync(byte[] audioData, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            if (audioData == null || audioData.Length == 0)
            {
                return new SpeechResult { ErrorMessage = "No audio data provided" };
            }

            if (audioData.Length > _config.MaxAudioSizeBytes)
            {
                return new SpeechResult { ErrorMessage = "Audio file too large" };
            }

            var text = await ProcessWithHuggingFaceAsync(audioData, cancellationToken);

            var result = new SpeechResult
            {
                Text = text,
                Confidence = CalculateConfidence(text),
                ProcessingTime = stopwatch.Elapsed
            };

            _logger.LogDebug("Hugging Face STT completed in {Time}ms: {Text}",
                stopwatch.ElapsedMilliseconds, text);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Hugging Face STT operation cancelled");
            return new SpeechResult { ErrorMessage = "Operation cancelled" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hugging Face STT processing failed");
            return new SpeechResult { ErrorMessage = ex.Message };
        }
        finally
        {
            _semaphore.Release();
            stopwatch.Stop();
        }
    }

    private async Task<string> ProcessWithHuggingFaceAsync(byte[] audioData, CancellationToken cancellationToken)
    {
        var apiUrl = $"https://api-inference.huggingface.co/models/{_config.ModelName}";

        using var content = new ByteArrayContent(audioData);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");

        var response = await _httpClient.PostAsync(apiUrl, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);

            // Model loading durumu kontrolü
            if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                var errorResponse = JsonSerializer.Deserialize<HuggingFaceErrorResponse>(errorContent);
                if (errorResponse?.Error?.Contains("loading") == true)
                {
                    // Model yükleniyor, bekle ve tekrar dene
                    _logger.LogInformation("Model loading, waiting {Seconds} seconds...",
                        errorResponse.EstimatedTime ?? 20);

                    await Task.Delay(TimeSpan.FromSeconds(errorResponse.EstimatedTime ?? 20), cancellationToken);
                    return await ProcessWithHuggingFaceAsync(audioData, cancellationToken);
                }
            }

            throw new Exception($"Hugging Face API error: {response.StatusCode} - {errorContent}");
        }

        var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);

        // Farklı model tiplerinin response formatları
        if (_config.ModelName.Contains("whisper"))
        {
            var whisperResponse = JsonSerializer.Deserialize<HuggingFaceWhisperResponse>(jsonResponse);
            return whisperResponse?.Text ?? string.Empty;
        }
        else
        {
            var genericResponse = JsonSerializer.Deserialize<HuggingFaceGenericResponse>(jsonResponse);
            return genericResponse?.TranscriptionText ?? string.Empty;
        }
    }

    private float CalculateConfidence(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0f;

        var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var hasValidStructure = wordCount > 0 && !text.Contains("...");

        return hasValidStructure ? Math.Min(0.85f + (wordCount * 0.015f), 1.0f) : 0.3f;
    }

    public void Dispose()
    {
        _semaphore?.Dispose();
    }
}


