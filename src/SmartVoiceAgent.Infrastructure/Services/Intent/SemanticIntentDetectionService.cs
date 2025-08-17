using Core.CrossCuttingConcerns.Logging.Serilog;
using Microsoft.Extensions.Configuration;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Models;
using System.Text;
using System.Text.Json;

namespace SmartVoiceAgent.Infrastructure.Services.Intent;
public class SemanticIntentDetectionService : IIntentDetectionService
{
    private readonly HttpClient _httpClient;
    private readonly LoggerServiceBase _logger;
    private readonly string _openRouterApiKey;
    private readonly Dictionary<CommandType, List<string>> _intentExamples;

    public SemanticIntentDetectionService(HttpClient httpClient, LoggerServiceBase logger, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _openRouterApiKey = configuration.GetSection("OpenRouter:ApiKey").Get<string>()
            ?? throw new NullReferenceException("OpenRouter ApiKey not found in configuration.");

        _intentExamples = LoadIntentExamples();
    }

    public async Task<IntentResult> DetectIntentAsync(string text, string language, CancellationToken cancellationToken = default)
    {
        try
        {
            var similarities = new Dictionary<CommandType, float>();

            foreach (var intentExample in _intentExamples)
            {
                var maxSimilarity = 0f;

                foreach (var example in intentExample.Value)
                {
                    var similarity = await CalculateSemanticSimilarity(text, example);
                    maxSimilarity = Math.Max(maxSimilarity, similarity);
                }

                similarities[intentExample.Key] = maxSimilarity;
            }

            var bestMatch = similarities.OrderByDescending(s => s.Value).First();

            if (bestMatch.Value < 0.6f)
            {
                return new IntentResult
                {
                    Intent = CommandType.Unknown,
                    Confidence = bestMatch.Value,
                    Language = language,
                    OriginalText = text
                };
            }

            return new IntentResult
            {
                Intent = bestMatch.Key,
                Confidence = bestMatch.Value,
                Entities = await ExtractEntitiesAsync(text, bestMatch.Key),
                Language = language,
                OriginalText = text
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"Semantic intent detection failed: {ex.Message}");
            return new IntentResult { Intent = CommandType.Unknown, OriginalText = text, Language = language };
        }
    }

    private async Task<float> CalculateSemanticSimilarity(string text1, string text2)
    {
        var prompt = $@"Rate the semantic similarity between these two phrases on a scale of 0.0 to 1.0:

Phrase 1: ""{text1}""
Phrase 2: ""{text2}""

Consider:
- Intent similarity (both asking to open an app = high similarity)
- Semantic meaning (different words, same meaning = high similarity)
- Context (Turkish and English equivalents = high similarity)

Respond with only a decimal number between 0.0 and 1.0:";

        try
        {
            var response = await CallOpenRouterForSimilarity(prompt);
            if (float.TryParse(response.Trim(), out var similarity))
            {
                return Math.Max(0f, Math.Min(1f, similarity));
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Similarity calculation failed: {ex.Message}");
        }

        // Fallback to Levenshtein similarity
        return CalculateLevenshteinSimilarity(text1, text2);
    }

    private Dictionary<CommandType, List<string>> LoadIntentExamples()
    {
        return new Dictionary<CommandType, List<string>>
        {
            [CommandType.OpenApplication] = new List<string>
            {
                "Spotify'i aç", "Chrome'u başlat", "Notepad'i çalıştır",
                "Open Spotify", "Start Chrome", "Launch Notepad",
                "Uygulama aç", "Program başlat", "Yazılım çalıştır"
            },
            [CommandType.PlayMusic] = new List<string>
            {
                "Müzik çal", "Şarkı oynat", "Müzik başlat",
                "Play music", "Start song", "Play some music",
                "Spotify'da müzik çal", "Müzik dinlemek istiyorum"
            },
            [CommandType.CloseApplication] = new List<string>
            {
                "Uygulamayı kapat", "Chrome'u sonlandır", "Programı durdur",
                "Close app", "Stop application", "Kill process",
                "Spotify'i kapat", "Müziği durdur"
            },
            [CommandType.SearchWeb] = new List<string>
            {
                "Google'da ara", "İnternette bul", "Araştır",
                "Search Google", "Find on web", "Look up",
                "Hava durumu ara", "Haberleri bul"
            }
        };
    }

    private async Task<string> CallOpenRouterForSimilarity(string prompt)
    {
        var requestBody = new
        {
            model = "microsoft/wizardlm-2-8x22b",
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            max_tokens = 10,
            temperature = 0.1
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Add("Authorization", $"Bearer {_openRouterApiKey}");
        request.Headers.Add("HTTP-Referer", "https://esquetta.netlify.app/");

        var response = await _httpClient.SendAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();

        var apiResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return apiResponse?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? "0.0";
    }

    private float CalculateLevenshteinSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0f;

        var distance = LevenshteinDistance(s1, s2);
        var maxLength = Math.Max(s1.Length, s2.Length);
        return 1.0f - (float)distance / maxLength;
    }

    private int LevenshteinDistance(string s1, string s2)
    {
        var matrix = new int[s1.Length + 1, s2.Length + 1];

        for (int i = 0; i <= s1.Length; i++) matrix[i, 0] = i;
        for (int j = 0; j <= s2.Length; j++) matrix[0, j] = j;

        for (int i = 1; i <= s1.Length; i++)
        {
            for (int j = 1; j <= s2.Length; j++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(Math.Min(
                    matrix[i - 1, j] + 1,
                    matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[s1.Length, s2.Length];
    }

    private async Task<Dictionary<string, object>> ExtractEntitiesAsync(string text, CommandType intent)
    {
        // Implementation similar to existing entity extraction but enhanced with AI
        return new Dictionary<string, object>();
    }
}
