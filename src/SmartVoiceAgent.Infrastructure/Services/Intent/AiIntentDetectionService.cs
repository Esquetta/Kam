using Core.CrossCuttingConcerns.Logging.Serilog;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Models.Intent;
using System.Text.Json;

namespace SmartVoiceAgent.Infrastructure.Services.Intent;
public class AiIntentDetectionService : IIntentDetectionService
{
    private readonly IChatClient _chatClient;
    private readonly LoggerServiceBase _logger;
    private readonly IIntentDetectionService _fallbackService; // Keep existing service as fallback

    public AiIntentDetectionService(
        IChatClient chatClient,
        LoggerServiceBase logger,
        IntentDetectorService fallbackService)
    {
        _chatClient = chatClient;
        _logger = logger;
        _fallbackService = fallbackService;
    }

    public async Task<IntentResult> DetectIntentAsync(string text, string language, CancellationToken cancellationToken = default)
    {
        try
        {
            var systemMessage = BuildSystemPrompt(language);
            var userMessage = $"Analyze this user input: \"{text}\"";

            var aiResponse = await CallAiProviderAsync(systemMessage, userMessage, cancellationToken);
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

    private async Task<string> CallAiProviderAsync(
        string systemMessage,
        string userMessage,
        CancellationToken cancellationToken)
    {
        var messages = new[]
        {
            new Microsoft.Extensions.AI.ChatMessage(ChatRole.System, systemMessage),
            new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, userMessage)
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        return string.Join(
                Environment.NewLine,
                response.Messages
                    .Select(message => message.Text)
                    .Where(message => !string.IsNullOrWhiteSpace(message)))
            .Trim();
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
