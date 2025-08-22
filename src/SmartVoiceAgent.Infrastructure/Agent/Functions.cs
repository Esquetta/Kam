using AutoGen.Core;
using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Commands;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Interfaces;
using System.Text.Json;

public partial class Functions
{
    private readonly IMediator _mediator;
    private readonly IIntentDetectionService _intentDetectorService;
    private readonly ICommandHandlerService _commandHandlerService;

    public Functions(IMediator mediator, IIntentDetectionService intentDetectorService, ICommandHandlerService commandHandlerService)
    {
        _mediator = mediator;
        _intentDetectorService = intentDetectorService;
        _commandHandlerService = commandHandlerService;
    }

    /// <summary>
    /// Universal voice command processor that handles any type of user request.
    /// This is the main entry point for processing all voice commands including polite requests,
    /// direct commands, and questions in Turkish and English.
    /// 
    /// Handles various command types:
    /// - Application control: "Chrome aç", "Chrome açar mısın?", "Lütfen Notepad başlat"
    /// - Music control: "Müzik çal", "Şarkı durdur", "Ses seviyesini artır"
    /// - Device control: "Bluetooth aç", "WiFi kapat", "Ekran parlaklığını azalt"
    /// - Web search: "Google'da hava durumu ara", "Web'de teknoloji haberleri aratır mısın?"
    /// - And other commands based on intent detection
    /// 
    /// The function automatically detects intent and routes to appropriate actions.
    /// </summary>
    /// <param name="userInput">The complete user speech input</param>
    /// <param name="language">Language code (tr, en, etc.)</param>
    /// <param name="context">Optional context information</param>
    /// <returns>JSON result of the executed command</returns>
    [Function]
    public async Task<string> ProcessVoiceCommandAsync(string userInput, string language = "tr", string context = null)
    {
        Console.WriteLine($"Processing voice command: {userInput}");

        try
        {
            // 1. Detect intent using existing service
            var intentResult = await _intentDetectorService.DetectIntentAsync(userInput, language);

            // 2. Build dynamic command request
            var commandRequest = new DynamicCommandRequest
            {
                Intent = intentResult.Intent.ToString(),
                Entities = intentResult.Entities,
                OriginalText = userInput,
                Language = language,
                Context = context
            };

            // 3. Execute command through dynamic handler
            var result = await _commandHandlerService.ExecuteCommandAsync(commandRequest);

            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ProcessVoiceCommandAsync: {ex.Message}");
            return JsonSerializer.Serialize(new CommandResult
            {
                Success = false,
                Message = "Komut işlenirken hata oluştu",
                Error = ex.Message,
                OriginalInput = userInput
            });
        }
    }

    /// <summary>
    /// Gets available voice commands and their descriptions.
    /// Useful for helping users understand what they can ask for.
    /// </summary>
    /// <param name="language">Language for command descriptions</param>
    /// <param name="category">Optional category filter (app_control, web, system, etc.)</param>
    /// <returns>JSON list of available commands with examples</returns>
    [Function]
    public async Task<string> GetAvailableCommandsAsync(string language = "tr", string category = null)
    {
        var commands = await _commandHandlerService.GetAvailableCommandsAsync(language, category);
        return JsonSerializer.Serialize(commands, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    /// <summary>
    /// Opens a desktop application based on the given name.
    /// </summary>
    /// <param name="applicationName">The name of the application to open</param>
    /// <returns>A result string about the operation status</returns>
    [Function]
    public async Task<string> OpenApplicationAsync(string applicationName)
    {
        Console.WriteLine($"Open application called directly with: {applicationName}");
        var result = await _mediator.Send(new OpenApplicationCommand(applicationName));
        return JsonSerializer.Serialize(result);
    }

    /// <summary>
    /// Closes a desktop application based on the given name.
    /// </summary>
    /// <param name="applicationName">The name of the application to close</param>
    /// <returns>A result string about the operation status</returns>
    [Function]
    public async Task<string> CloseApplicationAsync(string applicationName)
    {
        Console.WriteLine($"Close application called with: {applicationName}");
        var result = await _mediator.Send(new CloseApplicationCommand(applicationName));
        return JsonSerializer.Serialize(result);
    }

    /// <summary>
    /// Plays music based on the given track name.
    /// </summary>
    /// <param name="trackName">The name or path of the track to play</param>
    /// <returns>A result string about the operation status</returns>
    [Function]
    public async Task<string> PlayMusicAsync(string trackName)
    {
        Console.WriteLine($"Play music called with: {trackName}");
        var result = await _mediator.Send(new PlayMusicCommand(trackName));
        return JsonSerializer.Serialize(result);
    }

    /// <summary>
    /// Controls a device based on the given device name and action.
    /// </summary>
    /// <param name="deviceName">The name of the device to control</param>
    /// <param name="action">The action to perform on the device</param>
    /// <returns>A result string about the operation status</returns>
    [Function]
    public async Task<string> ControlDeviceAsync(string deviceName, string action)
    {
        Console.WriteLine($"Control device called with: {deviceName}, action: {action}");
        var result = await _mediator.Send(new ControlDeviceCommand(deviceName, action));
        return JsonSerializer.Serialize(result);
    }

    /// <summary>
    /// Detects the user's intent based on the given text input and language.
    /// </summary>
    /// <param name="text">The user's input text</param>
    /// <param name="language">The language of the input text</param>
    /// <returns>JSON string containing detected intent information</returns>
    [Function]
    public async Task<string> DetectIntentAsync(string text, string language)
    {
        var intent = await _intentDetectorService.DetectIntentAsync(text, language);
        return JsonSerializer.Serialize(intent, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    /// <summary>
    /// Searches the web for the given query and opens results on user's default browser.
    /// </summary>
    /// <param name="query">Search Query string</param>
    /// <param name="lang">Search Language</param>
    /// <param name="results">Results count</param>
    /// <returns>Returns command result as json</returns>
    [Function]
    public async Task<string> SearchWebAsync(string query, string lang = "tr", int results = 5)
    {
        Console.WriteLine($"Search web called with query: {query}");
        var command = new SearchWebCommand(query, lang, results);
        var result = await _mediator.Send(command);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }
    /// <summary>
    /// Checks if an application is installed and provides detailed information about it.
    /// This method verifies application installation status and returns comprehensive details.
    /// </summary>
    /// <param name="applicationName">The name of the application to check</param>
    /// <returns>JSON response containing installation status, path, and additional details</returns>
    [Function]
    public async Task<string> CheckApplicationAsync(string applicationName)
    {
        Console.WriteLine($"Check application called with: {applicationName}");

        try
        {
            var command = new CheckApplicationCommand(applicationName);
            var result = await _mediator.Send(command);

            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking application {applicationName}: {ex.Message}");
            return JsonSerializer.Serialize(new AgentApplicationResponse(
                Success: false,
                Message: $"Uygulama kontrol edilirken hata oluştu: {ex.Message}",
                ApplicationName: applicationName
            ), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        }
    }

    /// <summary>
    /// Gets the executable path of an installed application.
    /// Returns the full path to the application's executable file if found.
    /// </summary>
    /// <param name="applicationName">The name of the application</param>
    /// <returns>JSON response containing the executable path or error information</returns>
    [Function]
    public async Task<string> GetApplicationPathAsync(string applicationName)
    {
        Console.WriteLine($"Get application path called with: {applicationName}");

        try
        {
            var command = new GetApplicationPathCommand(applicationName);
            var result = await _mediator.Send(command);

            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting application path for {applicationName}: {ex.Message}");
            return JsonSerializer.Serialize(new AgentApplicationResponse(
                Success: false,
                Message: $"Uygulama yolu alınırken hata oluştu: {ex.Message}",
                ApplicationName: applicationName
            ), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        }
    }

    /// <summary>
    /// Lists all installed applications on the system.
    /// Provides a comprehensive list of available applications with their details.
    /// </summary>
    /// <param name="includeSystemApps">Whether to include system applications in the list</param>
    /// <returns>JSON response containing the list of installed applications</returns>
    [Function]
    public async Task<string> ListInstalledApplicationsAsync(bool includeSystemApps = false)
    {
        Console.WriteLine($"List installed applications called with includeSystemApps: {includeSystemApps}");

        try
        {
            var command = new ListInstalledApplicationsCommand(includeSystemApps);
            var result = await _mediator.Send(command);

            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error listing installed applications: {ex.Message}");
            return JsonSerializer.Serialize(new CommandResult
            {
                Success = false,
                Message = $"Yüklü uygulamalar listelenirken hata oluştu: {ex.Message}",
                Error = ex.Message
            }, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        }
    }

    /// <summary>
    /// Checks if an application is currently running on the system.
    /// Provides real-time status information about application execution state.
    /// </summary>
    /// <param name="applicationName">The name of the application to check</param>
    /// <returns>JSON response indicating if the application is running</returns>
    [Function]
    public async Task<string> IsApplicationRunningAsync(string applicationName)
    {
        Console.WriteLine($"Is application running called with: {applicationName}");

        try
        {
            var command = new IsApplicationRunningCommand(applicationName);
            var result = await _mediator.Send(command);

            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking if application {applicationName} is running: {ex.Message}");
            return JsonSerializer.Serialize(new AgentApplicationResponse(
                Success: false,
                Message: $"Uygulama çalışma durumu kontrol edilirken hata oluştu: {ex.Message}",
                ApplicationName: applicationName
            ), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        }
    }
    /// <summary>
    /// Controls system volume level (not just music playback volume)
    /// </summary>
    /// <param name="action">Action: increase, decrease, mute, unmute, set</param>
    /// <param name="level">Volume level (0-100) when action is 'set'</param>
    /// <returns>JSON result of volume control operation</returns>
    [Function]
    public async Task<string> ControlSystemVolumeAsync(string action, int level = 50)
    {
        Console.WriteLine($"Control system volume called with action: {action}, level: {level}");
        var command = new ControlSystemVolumeCommand(action, level);
        var result = await _mediator.Send(command);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    /// <summary>
    /// Controls screen brightness
    /// </summary>
    /// <param name="action">Action: increase, decrease, set</param>
    /// <param name="level">Brightness level (0-100) when action is 'set'</param>
    /// <returns>JSON result of brightness control operation</returns>
    [Function]
    public async Task<string> ControlScreenBrightnessAsync(string action, int level = 50)
    {
        Console.WriteLine($"Control screen brightness called with action: {action}, level: {level}");
        var command = new ControlScreenBrightnessCommand(action, level);
        var result = await _mediator.Send(command);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    /// <summary>
    /// Controls WiFi connection
    /// </summary>
    /// <param name="action">Action: enable, disable, toggle, status</param>
    /// <returns>JSON result of WiFi control operation</returns>
    [Function]
    public async Task<string> ControlWiFiAsync(string action)
    {
        Console.WriteLine($"Control WiFi called with action: {action}");
        var command = new ControlWiFiCommand(action);
        var result = await _mediator.Send(command);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    /// <summary>
    /// Controls Bluetooth connection
    /// </summary>
    /// <param name="action">Action: enable, disable, toggle, status</param>
    /// <returns>JSON result of Bluetooth control operation</returns>
    [Function]
    public async Task<string> ControlBluetoothAsync(string action)
    {
        Console.WriteLine($"Control Bluetooth called with action: {action}");
        var command = new ControlBluetoothCommand(action);
        var result = await _mediator.Send(command);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    /// <summary>
    /// Controls system power options
    /// </summary>
    /// <param name="action">Action: shutdown, restart, sleep, hibernate, lock</param>
    /// <param name="delayMinutes">Delay in minutes before executing action</param>
    /// <returns>JSON result of power control operation</returns>
    [Function]
    public async Task<string> ControlSystemPowerAsync(string action, int delayMinutes = 0)
    {
        Console.WriteLine($"Control system power called with action: {action}, delay: {delayMinutes}");
        var command = new ControlSystemPowerCommand(action, delayMinutes);
        var result = await _mediator.Send(command);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    /// <summary>
    /// Gets current system status information
    /// </summary>
    /// <param name="infoType">Type of info: volume, brightness, wifi, bluetooth, battery, all</param>
    /// <returns>JSON result containing system status information</returns>
    [Function]
    public async Task<string> GetSystemStatusAsync(string infoType = "all")
    {
        Console.WriteLine($"Get system status called with info type: {infoType}");
        var command = new GetSystemStatusCommand(infoType);
        var result = await _mediator.Send(command);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }


}

