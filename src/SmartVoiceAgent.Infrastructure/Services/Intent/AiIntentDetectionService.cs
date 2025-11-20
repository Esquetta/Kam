using Core.CrossCuttingConcerns.Logging.Serilog;
using Microsoft.Extensions.Configuration;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Models;
using SmartVoiceAgent.Core.Models.Intent;
using System.Text;
using System.Text.Json;

namespace SmartVoiceAgent.Infrastructure.Services.Intent;
public class AiIntentDetectionService : IIntentDetectionService
{
    private readonly HttpClient _httpClient;
    private readonly LoggerServiceBase _logger;
    private readonly string _openRouterApiKey;
    private readonly string _model;
    private readonly IIntentDetectionService _fallbackService; // Keep existing service as fallback

    public AiIntentDetectionService(
        HttpClient httpClient,
        LoggerServiceBase logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;

        _openRouterApiKey = configuration.GetSection("OpenRouter:ApiKey").Get<string>()
            ?? throw new NullReferenceException("OpenRouter ApiKey not found in configuration.");
        _model = configuration.GetSection("OpenRouter:Model").Get<string>() ?? "microsoft/wizardlm-2-8x22b";
    }

    public async Task<IntentResult> DetectIntentAsync(string text, string language, CancellationToken cancellationToken = default)
    {
        try
        {
            var systemMessage = BuildSystemPrompt(language);
            var userMessage = $"Analyze this user input: \"{text}\"";

            var aiResponse = await CallOpenRouterAsync(systemMessage, userMessage);
            var intentResult = ParseAiResponse(aiResponse, text, language);     
            return intentResult;
        }
        catch (Exception ex)
        {
            _logger.Error($"AI intent detection failed: {ex.Message}");
            // Fallback to pattern-based detection
            return await _fallbackService.DetectIntentAsync(text, language, cancellationToken);
        }
    }

    private string BuildSystemPrompt(string language)
    {
        var availableIntents = string.Join(", ", Enum.GetNames<CommandType>());

        return $@"You are an advanced intent detection system for a smart voice assistant.

Available intents: {availableIntents}

Your task is to analyze user input and determine:
1. The most likely intent
2. Confidence level (0.0-1.0)
3. Extracted entities (application names, parameters, etc.)

Special rules:
- ""Spotify'i aç"", ""Chrome'u başlat"" = OpenApplication (high confidence)
- ""Müzik çal"", ""Şarkı oynat"" = PlayMusic (high confidence)
- ""Google'da ara"", ""... bul"" = SearchWeb
- Application names should be extracted as entities
- Consider context and Turkish language nuances

Response format (JSON only):
{{
    ""intent"": ""CommandType"",
    ""confidence"": 0.95,
    ""entities"": {{
        ""applicationName"": ""Spotify"",
        ""action"": ""open""
    }},
    ""reasoning"": ""brief explanation""
}}

Language: {language}";
    }

    private async Task<string> CallOpenRouterAsync(string systemMessage, string userMessage)
    {
        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemMessage },
                new { role = "user", content = userMessage }
            },
            max_tokens = 500,
            temperature = 0.3 // Lower temperature for more consistent intent detection
        };

        var json = JsonSerializer.Serialize(requestBody);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Add("Authorization", $"Bearer {_openRouterApiKey}");
        request.Headers.Add("HTTP-Referer", "https://esquetta.netlify.app/");
        request.Headers.Add("X-Title", "Smart Voice Agent Intent Detection");

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"OpenRouter API error: {response.StatusCode} - {errorContent}");
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        var apiResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return apiResponse?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? "";
    }

    private IntentResult ParseAiResponse(string aiResponse, string originalText, string language)
    {
        try
        {
            // Clean the response (similar to your existing CleanJsonResponse method)
            var cleanResponse = CleanJsonResponse(aiResponse);

            var aiResult = JsonSerializer.Deserialize<AiIntentResponse>(cleanResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (aiResult != null && Enum.TryParse<CommandType>(aiResult.Intent, out var commandType))
            {
                return new IntentResult
                {
                    Intent = commandType,
                    Confidence = aiResult.Confidence,
                    Entities = aiResult.Entities ?? new Dictionary<string, object>(),
                    Language = language,
                    OriginalText = originalText
                };
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to parse AI response: {ex.Message}. Response: {aiResponse}");
        }

        return new IntentResult
        {
            Intent = CommandType.Unknown,
            Confidence = 0.0f,
            Language = language,
            OriginalText = originalText
        };
    }

    private string CleanJsonResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return "{}";

        response = response.Replace("```json", "").Replace("```", "").Trim();

        var jsonStartIndex = response.IndexOf('{');
        var jsonEndIndex = response.LastIndexOf('}');

        if (jsonStartIndex >= 0 && jsonEndIndex > jsonStartIndex)
        {
            response = response.Substring(jsonStartIndex, jsonEndIndex - jsonStartIndex + 1);
        }

        return response.StartsWith('{') ? response : "{}";
    }
}
