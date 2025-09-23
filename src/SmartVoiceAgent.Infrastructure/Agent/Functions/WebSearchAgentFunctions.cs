using AutoGen.Core;
using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Interfaces;
using System.Text.Json;

namespace SmartVoiceAgent.Infrastructure.Agent.Functions;

public partial class WebSearchAgentFunctions : IAgentFunctions
{
    private readonly IMediator _mediator;

    public WebSearchAgentFunctions(IMediator mediator)
    {
        _mediator = mediator;
    }

    [Function]
    public async Task<string> SearchWebAsync(string query, string lang = "tr", int results = 5)
    {
        Console.WriteLine($"WebSearchAgent: Searching web for {query}");
        try
        {
            var result = await _mediator.Send(new SearchWebCommand(query, lang, results));
            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Message = ex.Message });
        }
    }

    public IDictionary<string, Func<string, Task<string>>> GetFunctionMap()
    {
        return new Dictionary<string, Func<string, Task<string>>>
        {
            ["SearchWebAsync"] = async (args) =>
            {
                try
                {
                    var jsonArgs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(args);

                    // Query değerini güvenli şekilde al
                    var query = jsonArgs.ContainsKey("query") && jsonArgs["query"].ValueKind == JsonValueKind.String
                        ? jsonArgs["query"].GetString() ?? ""
                        : "";

                    // Language değerini güvenli şekilde al
                    var lang = "tr";
                    if (jsonArgs.ContainsKey("lang") && jsonArgs["lang"].ValueKind == JsonValueKind.String)
                    {
                        lang = jsonArgs["lang"].GetString() ?? "tr";
                    }

                    // Results değerini güvenli şekilde al
                    var results = 5;
                    if (jsonArgs.ContainsKey("results"))
                    {
                        if (jsonArgs["results"].ValueKind == JsonValueKind.Number)
                        {
                            results = jsonArgs["results"].GetInt32();
                        }
                        else if (jsonArgs["results"].ValueKind == JsonValueKind.String)
                        {
                            if (int.TryParse(jsonArgs["results"].GetString(), out var parsedResults))
                            {
                                results = parsedResults;
                            }
                        }
                    }

                    var result = await SearchWebAsync(query, lang, results);
                    return ParseJsonResponse(result, $"🔍 '{query}' araması tamamlandı");
                }
                catch (Exception ex)
                {
                    return $"❌ Arama hatası: {ex.Message}";
                }
            }
        };
    }

    /// <summary>
    /// Parses JSON response and extracts meaningful message
    /// </summary>
    private static string ParseJsonResponse(string jsonResult, string defaultMessage = "İşlem tamamlandı")
    {
        try
        {
            var jsonDocument = JsonDocument.Parse(jsonResult);
            var root = jsonDocument.RootElement;

            // Check for success field
            if (root.TryGetProperty("success", out var successElement))
            {
                var isSuccess = successElement.GetBoolean();

                if (isSuccess)
                {
                    // Try to get message
                    if (root.TryGetProperty("message", out var messageElement))
                    {
                        var message = messageElement.GetString();
                        return !string.IsNullOrEmpty(message) ? message : defaultMessage;
                    }

                    // Try to get result field
                    if (root.TryGetProperty("result", out var resultElement))
                    {
                        var result = resultElement.GetString();
                        return !string.IsNullOrEmpty(result) ? result : defaultMessage;
                    }

                    return defaultMessage;
                }
                else
                {
                    // Handle error case
                    if (root.TryGetProperty("error", out var errorElement))
                    {
                        return $"❌ {errorElement.GetString()}";
                    }

                    if (root.TryGetProperty("message", out var errorMessageElement))
                    {
                        return $"❌ {errorMessageElement.GetString()}";
                    }

                    return "❌ İşlem başarısız";
                }
            }

            // If no success field, try to extract any meaningful data
            if (root.TryGetProperty("message", out var directMessageElement))
            {
                return directMessageElement.GetString() ?? defaultMessage;
            }

            // If it's an array or complex object, return summary
            if (root.ValueKind == JsonValueKind.Array)
            {
                return $"✅ {root.GetArrayLength()} öğe döndürüldü";
            }

            return defaultMessage;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"⚠️ JSON parse hatası: {ex.Message}");
            return defaultMessage;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Response parse hatası: {ex.Message}");
            return defaultMessage;
        }
    }
}