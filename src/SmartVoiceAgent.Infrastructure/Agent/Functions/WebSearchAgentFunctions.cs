using AutoGen.Core;
using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Interfaces;
using System.Text.Json;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace SmartVoiceAgent.Infrastructure.Agent.Functions;

public class WebSearchAgentFunctions : IAgentFunctions
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
                    var jsonArgs = JsonSerializer.Deserialize<Dictionary<string, object>>(args);
                    var query = jsonArgs["query"]?.ToString() ?? "";
                    var lang = jsonArgs.ContainsKey("lang") ? jsonArgs["lang"]?.ToString() : "tr";
                    var results = jsonArgs.ContainsKey("results") ? Convert.ToInt32(jsonArgs["results"]) : 5;

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

    public FunctionContract SearchWebAsyncFunctionContract => new()
    {
        Name = nameof(SearchWebAsync),
        Description = "Searches the web for the given query and opens results on user's default browser",
        Parameters = (IEnumerable<FunctionParameterContract>)BinaryData.FromObjectAsJson(new
        {
            Type = "object",
            Properties = new
            {
                query = new
                {
                    Type = "string",
                    Description = "Search Query string"
                },
                lang = new
                {
                    Type = "string",
                    Description = "Search Language",
                    Default = "tr"
                },
                results = new
                {
                    Type = "integer",
                    Description = "Results count",
                    Default = 5
                }
            },
            Required = new[] { "query" }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
    };
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
