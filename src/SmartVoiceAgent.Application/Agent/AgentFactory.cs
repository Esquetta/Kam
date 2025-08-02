using AutoGen.Core;
using AutoGen.OpenAI;
using AutoGen.OpenAI.Extension;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

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

        var systemMessage = @"Sen akıllı bir sesli asistansın. 

=== ÇOK ÖNEMLİ: FUNCTION KULLANIM KURALLARI ===

1. **HER ZAMAN ProcessVoiceCommandAsync İLE BAŞLA**
   - Bu ana fonksiyondur ve TÜM komutları işleyebilir
   - Kullanıcı ne derse desin, İLK ÖNCE ProcessVoiceCommandAsync'i çağır
   - Diğer fonksiyonları sadece ProcessVoiceCommandAsync başarısız olursa kullan

2. **ÖRNEK KULLANIM**
   - Kullanıcı: ""Spotify'ı kapat""
   - Sen: ProcessVoiceCommandAsync(""Spotify'ı kapat"", ""tr"")
   
   - Kullanıcı: ""Chrome aç""  
   - Sen: ProcessVoiceCommandAsync(""Chrome aç"", ""tr"")

3. **FALLBACK KURALLAR** (ProcessVoiceCommandAsync başarısız olursa)
   - ""kapat"", ""close"" → CloseApplicationAsync
   - ""aç"", ""open"" → OpenApplicationAsync
   - ""çal"", ""play"" → PlayMusicAsync
   - ""ara"", ""search"" → SearchWebAsync

4. **YANIT FORMATI**
   - Önce ne yapacağını söyle: ""Spotify'ı kapatıyorum...""
   - Sonra uygun fonksiyonu çağır
   - Sonucu kullanıcıya açıkla

UNUTMA: ProcessVoiceCommandAsync universal bir fonksiyondur. HER ZAMAN İLK ÖNCE ONU KULLAN!";


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
                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MAIN FUNCTION] ProcessVoiceCommandAsync error: {ex.Message}");
                    return $"Error: {ex.Message}";
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
                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SPECIFIC] OpenApplicationAsync error: {ex.Message}");
                    return $"Error: {ex.Message}";
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
                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SPECIFIC] CloseApplicationAsync error: {ex.Message}");
                    return $"Error: {ex.Message}";
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
                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SPECIFIC] PlayMusicAsync error: {ex.Message}");
                    return $"Error: {ex.Message}";
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
                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SPECIFIC] SearchWebAsync error: {ex.Message}");
                    return $"Error: {ex.Message}";
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
                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SPECIFIC] ControlDeviceAsync error: {ex.Message}");
                    return $"Error: {ex.Message}";
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
                functions: [functions.ProcessVoiceCommandAsyncFunctionContract, functions.CloseApplicationAsyncFunctionContract, functions.OpenApplicationAsyncFunctionContract, functions.SearchWebAsyncFunctionContract, functions.DetectIntentAsyncFunctionContract, functions.DetectIntentAsyncFunctionContract],
                functionMap: functionMap))
            .RegisterPrintMessage();

        return agent;
    }
}
