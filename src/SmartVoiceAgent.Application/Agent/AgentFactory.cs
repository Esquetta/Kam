using AutoGen.Core;
using AutoGen.OpenAI;
using AutoGen.OpenAI.Extension;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text.Json;

/// <summary>
/// Factory class for creating and configuring intelligent agents with various system automation capabilities.
/// </summary>
public static class AgentFactory
{
    /// <summary>
    /// Creates an intelligent assistant agent with control over system applications, music playback,
    /// device automation, web search, and .NET coding capabilities.
    /// </summary>
    /// <param name="apikey">OpenAI API key</param>
    /// <param name="model">OpenAI model name (e.g., gpt-4)</param>
    /// <param name="endpoint">OpenAI endpoint URL</param>
    /// <param name="functions">A Functions instance that contains callable system functions</param>
    /// <returns>Configured <see cref="IAgent"/></returns>
    public static async Task<IAgent> CreateAdminAgentAsync(string apikey, string model, string endpoint, Functions functions)
    {
        Console.WriteLine("[DEBUG] Creating agent with improved function selection...");

        var systemMessage = @"Sen akıllı bir sesli asistansın. Kullanıcıların komutlarını anlayıp uygun aksiyonları alıyorsun.

=== FONKSİYON KULLANIM KURALLARI ===

1. **HER ZAMAN ProcessVoiceCommandAsync İLE BAŞLA**
   - Bu ana fonksiyondur ve TÜM komutları işleyebilir
   - Kullanıcı ne derse desin, İLK ÖNCE ProcessVoiceCommandAsync'i çağır
   - Bu fonksiyon başarısız olursa veya uygun değilse diğer spesifik fonksiyonları kullan

2. **YANIT FORMATI - ÇOK ÖNEMLİ**
   - Önce kullanıcıya ne yapacağını söyle: ""Spotify'ı kapatıyorum...""
   - Sonra uygun fonksiyonu çağır
   - Fonksiyon sonucunu JSON formatında değil, doğal dilde açıkla
   - Kullanıcıya asla ham JSON verisi gösterme

3. **ÖRNEK KONUŞMA**
   Kullanıcı: ""Spotify'ı kapat""
   Sen: ""Spotify uygulamasını kapatıyorum...""
   [ProcessVoiceCommandAsync çağır]
   Sen: ""Spotify başarıyla kapatıldı."" (JSON değil!)

4. **FALLBACK KURALLAR** (ProcessVoiceCommandAsync başarısız olursa)
   - ""kapat"", ""close"" → CloseApplicationAsync
   - ""aç"", ""open"" → OpenApplicationAsync
   - ""çal"", ""play"" → PlayMusicAsync
   - ""ara"", ""search"" → SearchWebAsync

5. **DAVRANIŞSAL KURALLAR**
   - Her zaman samimi ve yardımsever ol
   - Hataları kibarca açıkla
   - Alternatif çözümler öner
   - JSON formatında yanıt verme, doğal konuş

Unutma: Amacın kullanıcıya insan gibi yardım etmek, makine gibi JSON döndürmek değil!";

        var functionMap = new Dictionary<string, Func<string, Task<string>>>
        {
            ["ProcessVoiceCommandAsync"] = async (args) =>
            {
                Console.WriteLine($"[MAIN FUNCTION] ProcessVoiceCommandAsync called with: {args}");
                try
                {
                    var jsonArgs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(args);
                    var userInput = jsonArgs["userInput"]?.ToString() ?? "";
                    var language = jsonArgs.ContainsKey("language") ? jsonArgs["language"]?.ToString() : "tr";
                    var context = jsonArgs.ContainsKey("context") ? jsonArgs["context"]?.ToString() : null;

                    Console.WriteLine($"[MAIN FUNCTION] Calling ProcessVoiceCommandAsync: '{userInput}', lang: {language}");
                    var result = await functions.ProcessVoiceCommandAsync(userInput, language, context);
                    Console.WriteLine($"[MAIN FUNCTION] ProcessVoiceCommandAsync result: {result}");

                    // JSON sonucunu parse et ve doğal dilde döndür
                    try
                    {
                        var jsonResult = JsonDocument.Parse(result);
                        var root = jsonResult.RootElement;

                        if (root.TryGetProperty("success", out var successElement) && successElement.GetBoolean())
                        {
                            if (root.TryGetProperty("message", out var messageElement))
                            {
                                return messageElement.GetString() ?? "İşlem başarıyla tamamlandı.";
                            }
                        }
                        else if (root.TryGetProperty("error", out var errorElement))
                        {
                            return $"Hata oluştu: {errorElement.GetString()}";
                        }
                    }
                    catch
                    {
                        // JSON parse edilemezse direkt sonucu döndür
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MAIN FUNCTION] ProcessVoiceCommandAsync error: {ex.Message}");
                    return $"Komut işlenirken hata oluştu: {ex.Message}";
                }
            },

            ["OpenApplicationAsync"] = async (args) =>
            {
                Console.WriteLine($"[SPECIFIC] OpenApplicationAsync called with: {args}");
                try
                {
                    var jsonArgs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(args);
                    var appName = jsonArgs["applicationName"]?.ToString() ?? "";
                    Console.WriteLine($"[SPECIFIC] Opening application: {appName}");
                    var result = await functions.OpenApplicationAsync(appName);
                    Console.WriteLine($"[SPECIFIC] OpenApplicationAsync result: {result}");

                    // JSON'u parse et ve doğal dil yanıtı döndür
                    return ParseJsonToNaturalResponse(result, $"{appName} uygulaması açılıyor...");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SPECIFIC] OpenApplicationAsync error: {ex.Message}");
                    return $"Uygulama açılırken hata oluştu: {ex.Message}";
                }
            },

            ["CloseApplicationAsync"] = async (args) =>
            {
                Console.WriteLine($"[SPECIFIC] CloseApplicationAsync called with: {args}");
                try
                {
                    var jsonArgs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(args);
                    var appName = jsonArgs["applicationName"]?.ToString() ?? "";
                    Console.WriteLine($"[SPECIFIC] Closing application: {appName}");
                    var result = await functions.CloseApplicationAsync(appName);
                    Console.WriteLine($"[SPECIFIC] CloseApplicationAsync result: {result}");

                    return ParseJsonToNaturalResponse(result, $"{appName} uygulaması kapatılıyor...");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SPECIFIC] CloseApplicationAsync error: {ex.Message}");
                    return $"Uygulama kapatılırken hata oluştu: {ex.Message}";
                }
            },

            ["PlayMusicAsync"] = async (args) =>
            {
                Console.WriteLine($"[SPECIFIC] PlayMusicAsync called with: {args}");
                try
                {
                    var jsonArgs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(args);
                    var trackName = jsonArgs["trackName"]?.ToString() ?? "";
                    Console.WriteLine($"[SPECIFIC] Playing music: {trackName}");
                    var result = await functions.PlayMusicAsync(trackName);
                    Console.WriteLine($"[SPECIFIC] PlayMusicAsync result: {result}");

                    return ParseJsonToNaturalResponse(result, $"{trackName} çalınıyor...");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SPECIFIC] PlayMusicAsync error: {ex.Message}");
                    return $"Müzik çalarken hata oluştu: {ex.Message}";
                }
            },

            ["SearchWebAsync"] = async (args) =>
            {
                Console.WriteLine($"[SPECIFIC] SearchWebAsync called with: {args}");
                try
                {
                    var jsonArgs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(args);
                    var query = jsonArgs["query"]?.ToString() ?? "";
                    var lang = jsonArgs.ContainsKey("lang") ? jsonArgs["lang"]?.ToString() : "tr";
                    var results = jsonArgs.ContainsKey("results") ? Convert.ToInt32(jsonArgs["results"]) : 5;

                    Console.WriteLine($"[SPECIFIC] Searching web: {query}");
                    var result = await functions.SearchWebAsync(query, lang, results);
                    Console.WriteLine($"[SPECIFIC] SearchWebAsync result: {result}");

                    return ParseJsonToNaturalResponse(result, $"'{query}' için web araması yapılıyor...");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SPECIFIC] SearchWebAsync error: {ex.Message}");
                    return $"Web araması yapılırken hata oluştu: {ex.Message}";
                }
            },

            ["ControlDeviceAsync"] = async (args) =>
            {
                Console.WriteLine($"[SPECIFIC] ControlDeviceAsync called with: {args}");
                try
                {
                    var jsonArgs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(args);
                    var deviceName = jsonArgs["deviceName"]?.ToString() ?? "";
                    var action = jsonArgs["action"]?.ToString() ?? "";
                    Console.WriteLine($"[SPECIFIC] Controlling device: {deviceName}, action: {action}");
                    var result = await functions.ControlDeviceAsync(deviceName, action);
                    Console.WriteLine($"[SPECIFIC] ControlDeviceAsync result: {result}");

                    return ParseJsonToNaturalResponse(result, $"{deviceName} cihazında {action} işlemi yapılıyor...");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SPECIFIC] ControlDeviceAsync error: {ex.Message}");
                    return $"Cihaz kontrolü yapılırken hata oluştu: {ex.Message}";
                }
            }
        };

        var agent = new OpenAIChatAgent(
            chatClient: new ChatClient(
                model: model,
                credential: new ApiKeyCredential(apikey),
                options: new OpenAIClientOptions
                {
                    Endpoint = new Uri(endpoint)
                }),
            name: "KamAdmin",
            systemMessage: systemMessage)
            .RegisterMessageConnector()
            .RegisterMiddleware(new FunctionCallMiddleware(
                functions: [
                    functions.ProcessVoiceCommandAsyncFunctionContract,
                    functions.CloseApplicationAsyncFunctionContract,
                    functions.OpenApplicationAsyncFunctionContract,
                    functions.SearchWebAsyncFunctionContract,
                    functions.PlayMusicAsyncFunctionContract,
                    functions.ControlDeviceAsyncFunctionContract,
                    functions.DetectIntentAsyncFunctionContract
                ],
                functionMap: functionMap))
            .RegisterPrintMessage();

        return agent;
    }

    /// <summary>
    /// JSON yanıtını doğal dil yanıtına çevirir
    /// </summary>
    private static string ParseJsonToNaturalResponse(string jsonResult, string defaultMessage)
    {
        try
        {
            var jsonDocument = JsonDocument.Parse(jsonResult);
            var root = jsonDocument.RootElement;

            if (root.TryGetProperty("success", out var successElement) && successElement.GetBoolean())
            {
                if (root.TryGetProperty("message", out var messageElement))
                {
                    return messageElement.GetString() ?? defaultMessage;
                }
                return defaultMessage;
            }
            else if (root.TryGetProperty("error", out var errorElement))
            {
                return $"Hata: {errorElement.GetString()}";
            }
        }
        catch
        {
            // JSON parse edilemezse default mesajı döndür
        }

        return defaultMessage;
    }
}