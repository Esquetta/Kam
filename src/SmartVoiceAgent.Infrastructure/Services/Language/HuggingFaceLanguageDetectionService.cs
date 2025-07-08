using Core.CrossCuttingConcerns.Logging.Serilog;
using Microsoft.Extensions.Configuration;
using SmartVoiceAgent.Core.Config;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Infrastructure.Services.Language
{
    public class HuggingFaceLanguageDetectionService : ILanguageDetectionService
    {
        private readonly LoggerServiceBase _logger;
        private readonly HuggingFaceConfig _config;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _semaphore;

        public HuggingFaceLanguageDetectionService(
            LoggerServiceBase logger,
            HttpClient httpClient,
            IConfiguration configuration)
        {
            _logger = logger;
            _config = configuration.GetSection("HuggingFaceConfig").Get<HuggingFaceConfig>()
                ?? throw new NullReferenceException("HuggingFaceConfig section cannot found in configuration.");
            _httpClient = httpClient;
            _semaphore = new SemaphoreSlim(_config.MaxConcurrentRequests, _config.MaxConcurrentRequests);

            // Set Hugging Face API key
            if (!string.IsNullOrEmpty(_config.ApiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.ApiKey);
            }
        }

        public async Task<LanguageResult> DetectLanguageAsync(string text, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new LanguageResult { ErrorMessage = "No text provided for language detection" };
            }

            // Kısa metinler için hızlı tespit
            var quickDetection = QuickLanguageDetection(text);
            if (quickDetection.IsReliable)
            {
                _logger.Debug($"Quick language detection: {quickDetection.Language} (confidence: {quickDetection.Confidence})");
                return quickDetection;
            }

            await _semaphore.WaitAsync(cancellationToken);

            try
            {
                var result = await ProcessWithHuggingFaceAsync(text, cancellationToken);
                _logger.Debug($"Hugging Face language detection: {result.Language} (confidence: {result.Confidence})");
                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.Warn("Language detection operation cancelled");
                return new LanguageResult { ErrorMessage = "Operation cancelled" };
            }
            catch (Exception ex)
            {
                _logger.Error($"Language detection error: {ex.Message}");

                // Fallback olarak quick detection kullan
                if (quickDetection.Confidence > 0.5f)
                {
                    _logger.Info("Using quick detection as fallback");
                    return quickDetection;
                }

                return new LanguageResult { ErrorMessage = ex.Message };
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task<LanguageResult> ProcessWithHuggingFaceAsync(string text, CancellationToken cancellationToken)
        {
            // Hugging Face language detection model - facebook/fasttext-language-identification önerilir
            var apiUrl = $"https://api-inference.huggingface.co/models/facebook/fasttext-language-identification";

            var requestData = new { inputs = text };
            var json = JsonSerializer.Serialize(requestData);

            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

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
                        _logger.Info($"Language detection model loading, waiting {errorResponse.EstimatedTime ?? 20} seconds...");
                        await Task.Delay(TimeSpan.FromSeconds(errorResponse.EstimatedTime ?? 20), cancellationToken);
                        return await ProcessWithHuggingFaceAsync(text, cancellationToken);
                    }
                }

                throw new Exception($"Hugging Face Language Detection API error: {response.StatusCode} - {errorContent}");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
            var detectionResults = JsonSerializer.Deserialize<HuggingFaceLanguageDetectionResponse[]>(jsonResponse);

            if (detectionResults?.Length > 0)
            {
                var result = new LanguageResult
                {
                    Language = ExtractLanguageCode(detectionResults[0].Label),
                    Confidence = detectionResults[0].Score,
                    AlternativeLanguages = new Dictionary<string, float>()
                };

                // Alternatif dilleri ekle (ilk sonuç hariç)
                for (int i = 1; i < Math.Min(detectionResults.Length, 5); i++)
                {
                    var langCode = ExtractLanguageCode(detectionResults[i].Label);
                    if (!result.AlternativeLanguages.ContainsKey(langCode))
                    {
                        result.AlternativeLanguages[langCode] = detectionResults[i].Score;
                    }
                }

                return result;
            }

            return new LanguageResult { ErrorMessage = "No language detection results" };
        }

        private LanguageResult QuickLanguageDetection(string text)
        {
            // Basit pattern matching ile hızlı dil tespiti
            var cleanText = text.ToLowerInvariant();
            var result = new LanguageResult
            {
                Language = "en", // Varsayılan
                Confidence = 0.3f,
                AlternativeLanguages = new Dictionary<string, float>()
            };

            // Türkçe karakterler ve kelimeler
            if (Regex.IsMatch(cleanText, @"[çğıöşü]") ||
                ContainsWords(cleanText, new[] { "bir", "bu", "ve", "ile", "için", "olan", "var", "yok", "merhaba", "nasıl" }))
            {
                result.Language = "tr";
                result.Confidence = 0.9f;
                result.AlternativeLanguages["en"] = 0.1f;
                return result;
            }

            // İngilizce yaygın kelimeler
            if (ContainsWords(cleanText, new[] { "the", "and", "or", "but", "with", "have", "this", "that", "hello", "how" }))
            {
                result.Language = "en";
                result.Confidence = 0.7f;
                result.AlternativeLanguages["tr"] = 0.2f;
                return result;
            }

            // Arapça karakterler
            if (Regex.IsMatch(text, @"[\u0600-\u06FF]"))
            {
                result.Language = "ar";
                result.Confidence = 0.8f;
                result.AlternativeLanguages["en"] = 0.1f;
                result.AlternativeLanguages["tr"] = 0.1f;
                return result;
            }

            // Almanca karakterler ve kelimeler
            if (Regex.IsMatch(cleanText, @"[äöüß]") ||
                ContainsWords(cleanText, new[] { "der", "die", "das", "und", "mit", "haben", "sein", "werden" }))
            {
                result.Language = "de";
                result.Confidence = 0.8f;
                result.AlternativeLanguages["en"] = 0.2f;
                return result;
            }

            // Fransızca karakterler ve kelimeler
            if (Regex.IsMatch(cleanText, @"[àâäéèêëïîôöùûüÿç]") ||
                ContainsWords(cleanText, new[] { "le", "la", "les", "et", "avec", "avoir", "être", "pour" }))
            {
                result.Language = "fr";
                result.Confidence = 0.8f;
                result.AlternativeLanguages["en"] = 0.2f;
                return result;
            }

            // Düşük confidence ile varsayılan İngilizce
            result.AlternativeLanguages["tr"] = 0.3f;
            result.AlternativeLanguages["de"] = 0.2f;
            result.AlternativeLanguages["fr"] = 0.2f;

            return result;
        }

        private bool ContainsWords(string text, string[] words)
        {
            return words.Any(word => text.Contains($" {word} ") || text.StartsWith($"{word} ") || text.EndsWith($" {word}"));
        }

        private string ExtractLanguageCode(string label)
        {
            // Hugging Face model sonuçlarından dil kodunu çıkar
            // Örnek: "__label__en" -> "en"
            return label.Replace("__label__", "").Trim();
        }

        public void Dispose()
        {
            _semaphore?.Dispose();
        }
    }
}
