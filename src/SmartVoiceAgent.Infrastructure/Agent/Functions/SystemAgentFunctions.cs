using AutoGen.Core;
using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Commands;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models;
using System.Text.Json;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace SmartVoiceAgent.Infrastructure.Agent.Functions;

public class SystemAgentFunctions : IAgentFunctions
{
    private readonly IMediator _mediator;
    private readonly ConversationContextManager contextManager;

    public SystemAgentFunctions(IMediator mediator, ConversationContextManager contextManager)
    {
        _mediator = mediator;
        this.contextManager = contextManager;
    }

    [Function]
    public async Task<string> OpenApplicationAsync(string applicationName)
    {
        Console.WriteLine($"SystemAgent: Opening application {applicationName}");
        try
        {
            var result = await _mediator.Send(new OpenApplicationCommand(applicationName));
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

    [Function]
    public async Task<string> CloseApplicationAsync(string applicationName)
    {
        Console.WriteLine($"SystemAgent: Closing application {applicationName}");
        try
        {
            var result = await _mediator.Send(new CloseApplicationCommand(applicationName));
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

    [Function]
    public async Task<string> CheckApplicationAsync(string applicationName)
    {
        Console.WriteLine($"SystemAgent: Checking application {applicationName}");
        try
        {
            var result = await _mediator.Send(new CheckApplicationCommand(applicationName));
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

    [Function]
    public async Task<string> GetApplicationPathAsync(string applicationName)
    {
        Console.WriteLine($"SystemAgent: Getting path for application {applicationName}");
        try
        {
            var result = await _mediator.Send(new GetApplicationPathCommand(applicationName));
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

    [Function]
    public async Task<string> IsApplicationRunningAsync(string applicationName)
    {
        Console.WriteLine($"SystemAgent: Checking if application {applicationName} is running");
        try
        {
            var result = await _mediator.Send(new IsApplicationRunningCommand(applicationName));
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

    [Function]
    public async Task<string> ListInstalledApplicationsAsync(bool includeSystemApps = false)
    {
        Console.WriteLine($"SystemAgent: Listing installed applications (includeSystemApps: {includeSystemApps})");
        try
        {
            var result = await _mediator.Send(new ListInstalledApplicationsCommand(includeSystemApps));
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

    [Function]
    public async Task<string> PlayMusicAsync(string trackName)
    {
        Console.WriteLine($"SystemAgent: Playing music {trackName}");
        try
        {
            var result = await _mediator.Send(new PlayMusicCommand(trackName));
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

    [Function]
    public async Task<string> ControlDeviceAsync(string deviceName, string action)
    {
        Console.WriteLine($"SystemAgent: Controlling device {deviceName} with action {action}");
        try
        {
            var result = await _mediator.Send(new ControlDeviceCommand(deviceName, action));
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
            ["OpenApplicationAsync"] = async (args) =>
            {
                try
                {
                    var jsonArgs = JsonSerializer.Deserialize<Dictionary<string, object>>(args);
                    var appName = jsonArgs["applicationName"]?.ToString() ?? "";

                    // Check if app is already open
                    if (contextManager.IsApplicationOpen(appName))
                    {
                        return $"ℹ️ {appName} zaten açık";
                    }

                    var result = await OpenApplicationAsync(appName);

                    // Update application state on successful open
                    var parsedResult = TryParseJsonResult(result);
                    if (parsedResult?.Success == true)
                    {
                        contextManager.SetApplicationState(appName, true);
                    }

                    contextManager.UpdateContext("app_open", appName, result);
                    return ParseJsonResponse(result, $"✅ {appName} açıldı");
                }
                catch (Exception ex)
                {
                    contextManager.UpdateContext("app_open_error", args, ex.Message);
                    return $"❌ Uygulama açma hatası: {ex.Message}";
                }
            },

            ["CloseApplicationAsync"] = async (args) =>
            {
                try
                {
                    var jsonArgs = JsonSerializer.Deserialize<Dictionary<string, object>>(args);
                    var appName = jsonArgs["applicationName"]?.ToString() ?? "";

                    // Check if app is actually open
                    if (!contextManager.IsApplicationOpen(appName))
                    {
                        return $"ℹ️ {appName} zaten kapalı";
                    }

                    var result = await CloseApplicationAsync(appName);

                    // Update application state on successful close
                    var parsedResult = TryParseJsonResult(result);
                    if (parsedResult?.Success == true)
                    {
                        contextManager.SetApplicationState(appName, false);
                    }

                    contextManager.UpdateContext("app_close", appName, result);
                    return ParseJsonResponse(result, $"✅ {appName} kapatıldı");
                }
                catch (Exception ex)
                {
                    contextManager.UpdateContext("app_close_error", args, ex.Message);
                    return $"❌ Uygulama kapatma hatası: {ex.Message}";
                }
            },

            ["PlayMusicAsync"] = async (args) =>
            {
                try
                {
                    var jsonArgs = JsonSerializer.Deserialize<Dictionary<string, object>>(args);
                    var trackName = jsonArgs["trackName"]?.ToString() ?? "";

                    var result = await PlayMusicAsync(trackName);

                    contextManager.UpdateContext("music_play", trackName, result);
                    return ParseJsonResponse(result, $"🎵 Müzik çalıyor: {trackName}");
                }
                catch (Exception ex)
                {
                    contextManager.UpdateContext("music_play_error", args, ex.Message);
                    return $"❌ Müzik çalma hatası: {ex.Message}";
                }
            },

            ["ControlDeviceAsync"] = async (args) =>
            {
                try
                {
                    var jsonArgs = JsonSerializer.Deserialize<Dictionary<string, object>>(args);
                    var deviceName = jsonArgs["deviceName"]?.ToString() ?? "";
                    var action = jsonArgs["action"]?.ToString() ?? "";

                    var result = await ControlDeviceAsync(deviceName, action);

                    contextManager.UpdateContext("device_control", $"{deviceName}:{action}", result);
                    return ParseJsonResponse(result, $"📱 {deviceName} - {action} işlemi tamamlandı");
                }
                catch (Exception ex)
                {
                    contextManager.UpdateContext("device_control_error", args, ex.Message);
                    return $"❌ Cihaz kontrol hatası: {ex.Message}";
                }
            }        
      
        };
    }
    /// <summary>
    /// Helper method to safely parse JSON result for internal use
    /// </summary>
    private static CommandResultWrapper? TryParseJsonResult(string jsonResult)
    {
        try
        {
            var jsonDocument = JsonDocument.Parse(jsonResult);
            var root = jsonDocument.RootElement;

            var success = false;
            var message = "";
            var error = "";

            if (root.TryGetProperty("success", out var successElement))
            {
                success = successElement.GetBoolean();
            }

            if (root.TryGetProperty("message", out var messageElement))
            {
                message = messageElement.GetString() ?? "";
            }

            if (root.TryGetProperty("error", out var errorElement))
            {
                error = errorElement.GetString() ?? "";
            }

            return new CommandResultWrapper
            {
                Success = success,
                Message = message,
                Error = error
            };
        }
        catch
        {
            return null;
        }
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
