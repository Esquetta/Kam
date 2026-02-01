using Core.CrossCuttingConcerns.Logging.Serilog;
using Microsoft.Extensions.Configuration;
using SmartVoiceAgent.Core.Config;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models;
using SmartVoiceAgent.Core.Models.Audio;
using System.Diagnostics;
using System.Text.Json;

public class HuggingFaceSTTService : ISpeechToTextService
{
    private readonly LoggerServiceBase logger;
    private readonly HuggingFaceConfig _config;
    private readonly SemaphoreSlim _semaphore;
    private readonly HttpClient _httpClient;


    public HuggingFaceSTTService(
        LoggerServiceBase logger,
        HttpClient httpClient,
        IConfiguration configuration)
    {
        this.logger = logger;
        _config = configuration.GetSection("HuggingFaceConfig").Get<HuggingFaceConfig>()
            ?? throw new NullReferenceException($"\" HuggingFaceConfig section cannot found in configuration."); ;
        _semaphore = new SemaphoreSlim(_config.MaxConcurrentRequests, _config.MaxConcurrentRequests);
        _httpClient = httpClient;

        // Set Hugging Face API key
        if (!string.IsNullOrEmpty(_config.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.ApiKey);
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

            logger.Debug($"Hugging Face STT completed in {stopwatch.ElapsedMilliseconds}ms: {text}");

            return result;
        }
        catch (OperationCanceledException)
        {
            logger.Warn("Hugging Face STT operation cancelled");
            return new SpeechResult { ErrorMessage = "Operation cancelled" };
        }
        catch (Exception ex)
        {
            logger.Error(ex.Message);
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
        // Use the model from config, but default to a model that works with Router API
        var modelName = string.IsNullOrEmpty(_config.ModelName) 
            ? "openai/whisper-large-v3-turbo" 
            : _config.ModelName;
        
        var apiUrl = $"https://router.huggingface.co/hf-inference/models/{modelName}"; 

        // Convert raw PCM bytes to WAV format with proper headers
        var wavData = ConvertPcmToWav(audioData, 16000, 1, 16);

        using var content = new ByteArrayContent(wavData);
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
                    logger.Info($"Model loading, waiting Seconds seconds...{errorResponse.EstimatedTime ?? 20}");

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

    /// <summary>
    /// Converts raw PCM audio data to WAV format with proper headers
    /// </summary>
    private static byte[] ConvertPcmToWav(byte[] pcmData, int sampleRate, int channels, int bitsPerSample)
    {
        if (pcmData == null || pcmData.Length == 0)
            return Array.Empty<byte>();

        var byteRate = sampleRate * channels * (bitsPerSample / 8);
        var blockAlign = channels * (bitsPerSample / 8);
        var dataSize = pcmData.Length;
        var wavSize = 44 + dataSize;

        var wavData = new byte[wavSize];

        // RIFF chunk descriptor
        WriteBytes(wavData, 0, "RIFF");
        WriteInt32(wavData, 4, wavSize - 8); // File size - 8
        WriteBytes(wavData, 8, "WAVE");

        // fmt sub-chunk
        WriteBytes(wavData, 12, "fmt ");
        WriteInt32(wavData, 16, 16); // Subchunk1Size (16 for PCM)
        WriteInt16(wavData, 20, 1);  // AudioFormat (1 for PCM)
        WriteInt16(wavData, 22, (short)channels);
        WriteInt32(wavData, 24, sampleRate);
        WriteInt32(wavData, 28, byteRate);
        WriteInt16(wavData, 32, (short)blockAlign);
        WriteInt16(wavData, 34, (short)bitsPerSample);

        // data sub-chunk
        WriteBytes(wavData, 36, "data");
        WriteInt32(wavData, 40, dataSize);

        // Copy PCM data
        Buffer.BlockCopy(pcmData, 0, wavData, 44, dataSize);

        return wavData;
    }

    private static void WriteBytes(byte[] buffer, int offset, string value)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, buffer, offset, bytes.Length);
    }

    private static void WriteInt16(byte[] buffer, int offset, short value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
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


