using AutoGen.Core;
using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Commands;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models;
using System.Text.Json;

namespace SmartVoiceAgent.Infrastructure.Agent.Functions;

public partial class SystemAgentFunctions : IAgentFunctions
{
    private readonly IMediator _mediator;
    private readonly ConversationContextManager contextManager;

    public SystemAgentFunctions(IMediator mediator, ConversationContextManager contextManager)
    {
        _mediator = mediator;
        this.contextManager = contextManager;
    }
    /// <summary>
    /// SYSTEM AGENT - OPEN APPLICATION
    /// 
    /// Launches desktop applications through the system agent with context awareness.
    /// This method is specifically designed for the SystemAgent and includes:
    /// - Application state tracking and validation
    /// - Context manager integration for conversation history
    /// - Duplicate launch prevention
    /// - Error handling with user-friendly messages
    /// 
    /// SUPPORTED APPLICATIONS:
    /// 🌐 Browsers: Chrome, Firefox, Edge, Safari, Opera
    /// 📝 Office: Word, Excel, PowerPoint, Outlook, OneNote
    /// 🎵 Media: Spotify, VLC, Windows Media Player, YouTube Music
    /// 💻 Development: Visual Studio, VS Code, Notepad++, Git
    /// 🖥️ System: Calculator, Paint, Command Prompt, PowerShell
    /// 🎮 Gaming: Steam, Discord, Epic Games Launcher
    /// 
    /// CONTEXT INTEGRATION:
    /// - Checks if application is already running
    /// - Updates conversation context with launch results
    /// - Tracks application state for future commands
    /// - Provides intelligent duplicate detection
    /// 
    /// EXAMPLES:
    /// ✅ "Chrome aç" → Opens Google Chrome browser
    /// ✅ "Spotify başlat" → Launches Spotify music app
    /// ✅ "Word'ü açar mısın?" → Opens Microsoft Word
    /// </summary>
    /// <param name="applicationName">Name of the application to launch (e.g., "Chrome", "Spotify")</param>
    /// <returns>JSON result with launch status, application details, and context updates</returns>

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
    /// <summary>
    /// SYSTEM AGENT - CLOSE APPLICATION
    /// 
    /// Safely terminates running desktop applications through the system agent.
    /// This is the PRIMARY method for application closure with advanced features:
    /// - Graceful shutdown attempts before force termination
    /// - Context-aware closure (checks if app is actually running)
    /// - Multiple instance handling (closes all instances)
    /// - Save prompts and user data protection
    /// - Conversation context updates
    /// 
    /// CLOSURE METHODS:
    /// 🪟 Window Close: Sends WM_CLOSE message for graceful shutdown
    /// ⚡ Process Kill: Force terminates unresponsive applications
    /// 🔍 Smart Detection: Finds processes by name, title, or path
    /// 💾 Data Safety: Allows save dialogs before closure
    /// 
    /// APPLICATION CATEGORIES:
    /// 🌐 Browsers: Closes all tabs and saves session data
    /// 📝 Office Apps: Prompts to save unsaved documents
    /// 🎵 Media Players: Stops playback and saves playlists
    /// 🎮 Games: Allows save game progress
    /// 💻 IDEs: Preserves project state and unsaved files
    /// 
    /// CONTEXT FEATURES:
    /// - Verifies application is running before attempting closure
    /// - Updates application state tracking
    /// - Records closure results in conversation history
    /// - Prevents unnecessary closure attempts
    /// 
    /// EXAMPLES:
    /// ✅ "Chrome kapat" → Closes all Chrome windows
    /// ✅ "Spotify'ı sonlandır" → Stops music and closes Spotify
    /// ✅ "Word'ü kapatır mısın?" → Closes Word (with save prompt)
    /// ✅ "Tüm browser'ları kapat" → Closes all browser instances
    /// </summary>
    /// <param name="applicationName">Name of the application to close (e.g., "Chrome", "Spotify")</param>
    /// <returns>JSON result with closure status, affected processes, and context updates</returns>

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
    // <summary>
    /// SYSTEM AGENT - CHECK APPLICATION
    /// 
    /// Comprehensive application verification and diagnostic system.
    /// Performs deep inspection of application installation and configuration:
    /// 
    /// VERIFICATION CHECKS:
    /// ✅ Installation Status: Is the application properly installed?
    /// 📂 Path Validation: Where is the executable located?
    /// 🏷️ Version Detection: What version is installed?
    /// ⚙️ Registry Entries: Are system registrations correct?
    /// 🔗 Shortcuts: Are Start Menu/Desktop shortcuts present?
    /// 🔍 Dependencies: Are required components installed?
    /// 
    /// DIAGNOSTIC INFORMATION:
    /// 🖥️ System Compatibility: Works with current OS version?
    /// 📊 Resource Requirements: Memory and disk usage info
    /// 🔒 Permissions: Required security permissions
    /// 🌐 Network Access: Internet connectivity requirements
    /// 
    /// USE CASES:
    /// 🔧 Troubleshooting: "Chrome neden açılmıyor?"
    /// 📋 Inventory: "Hangi uygulamalar yüklü?"
    /// ✅ Verification: "Office yüklü mü?"
    /// 🛠️ Diagnostics: "Spotify'da sorun var mı?"
    /// 
    /// Returns detailed technical information for system administrators and power users.
    /// </summary>
    /// <param name="applicationName">Application name to inspect and verify</param>
    /// <returns>JSON with comprehensive application status, paths, versions, and diagnostic data</returns>

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
    /// <summary>
    /// SYSTEM AGENT - GET APPLICATION PATH
    /// 
    /// Advanced executable path discovery system for installed applications.
    /// Searches through multiple system locations to find application executables:
    /// 
    /// SEARCH LOCATIONS:
    /// 📁 Program Files: Standard installation directory
    /// 📂 Program Files (x86): 32-bit applications on 64-bit systems
    /// 🏠 User AppData: Per-user installed applications
    /// 🖥️ System32: Windows system applications
    /// 🌐 Windows Apps: Microsoft Store/UWP applications
    /// 📋 Registry Keys: Registered application paths
    /// 🔗 PATH Environment: System PATH directories
    /// 
    /// PATH TYPES RETURNED:
    /// 🎯 Executable Path: Direct .exe file location
    /// 📁 Installation Directory: Root application folder
    /// 🔗 Shortcut Targets: Resolved shortcut destinations
    /// ⚙️ Command Arguments: Default startup parameters
    /// 
    /// ADVANCED FEATURES:
    /// 🔍 Fuzzy Matching: Finds apps with partial names
    /// 🏷️ Version Detection: Multiple installed versions
    /// 🔄 Alternative Names: Handles app aliases and shortcuts
    /// 📊 Metadata Extraction: File version, publisher, description
    /// 
    /// PRACTICAL USES:
    /// 🚀 Automation: Launch apps with specific parameters
    /// 🔧 Troubleshooting: Verify correct installation paths
    /// 📋 Inventory: Catalog installed software locations
    /// ⚡ Performance: Direct executable access
    /// </summary>
    /// <param name="applicationName">Application name to locate in the file system</param>
    /// <returns>JSON with full executable path, installation directory, and metadata</returns>

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
    /// <summary>
    /// SYSTEM AGENT - IS APPLICATION RUNNING
    /// 
    /// Real-time application execution monitoring and process analysis.
    /// Provides comprehensive information about running application states:
    /// 
    /// DETECTION METHODS:
    /// 🔍 Process Name: Matches executable file names
    /// 🪟 Window Title: Identifies by window captions
    /// 📂 Executable Path: Verifies by full file paths
    /// 🏷️ Process ID: Tracks by unique process identifiers
    /// 
    /// RUNTIME INFORMATION:
    /// ⚡ Process Status: Running, suspended, not responding
    /// 🧠 Memory Usage: RAM consumption and working set
    /// ⚙️ CPU Usage: Processor utilization percentage
    /// 🪟 Window State: Visible, minimized, maximized, hidden
    /// 👤 User Context: Which user account owns the process
    /// ⏰ Uptime: How long the application has been running
    /// 
    /// MULTI-INSTANCE HANDLING:
    /// 📊 Instance Count: Number of running copies
    /// 🔗 Parent-Child: Process relationships and dependencies
    /// 🎯 Main Window: Primary application window identification
    /// 
    /// USE CASES:
    /// ✅ Status Check: "Chrome çalışıyor mu?"
    /// 🔧 Troubleshooting: "Neden yavaş çalışıyor?"
    /// 📊 Monitoring: System performance analysis
    /// 🎮 Gaming: Check if games are running
    /// 
    /// Essential for system monitoring, troubleshooting, and automated workflows.
    /// </summary>
    /// <param name="applicationName">Application name to monitor and analyze</param>
    /// <returns>JSON with execution status, process details, resource usage, and window information</returns>
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
    /// <summary>
    /// SYSTEM AGENT - LIST INSTALLED APPLICATIONS
    /// 
    /// Comprehensive system-wide application inventory and discovery system.
    /// Scans multiple sources to create complete software catalog:
    /// 
    /// DISCOVERY SOURCES:
    /// 📋 Windows Registry: Add/Remove Programs entries
    /// 🏪 Microsoft Store: UWP and Store applications
    /// 📁 Program Files: Traditional desktop applications
    /// 👤 User Directories: Per-user installed software
    /// 🔗 Start Menu: Application shortcuts and entries
    /// 🖥️ System Tools: Built-in Windows applications
    /// 
    /// APPLICATION CATEGORIES:
    /// 🌐 Web Browsers: Chrome, Firefox, Edge, Safari
    /// 📝 Productivity: Office suite, text editors, PDF readers
    /// 🎵 Multimedia: Music players, video editors, media codecs
    /// 🎮 Gaming: Game clients, engines, and launchers
    /// 💻 Development: IDEs, compilers, version control
    /// 🛡️ Security: Antivirus, firewalls, system tools
    /// 🎨 Creative: Image editors, design software, CAD tools
    /// 
    /// FILTERING OPTIONS:
    /// 🏠 User Apps Only: Hide Windows system components
    /// ⚙️ Include System: Show all installed software
    /// 🏪 Store Apps: Microsoft Store applications
    /// 📊 Sort Options: Name, install date, size, publisher
    /// 
    /// RETURNED INFORMATION:
    /// 🏷️ Application Name and Display Name
    /// 📊 Version and Build Information
    /// 👥 Publisher and Developer Details
    /// 📅 Installation Date and Last Modified
    /// 💾 Installation Size and Disk Usage
    /// 📂 Installation Path and Executable Location
    /// 
    /// Perfect for system inventory, software audits, and application management.
    /// </summary>
    /// <param name="includeSystemApps">Include Windows system applications and components</param>
    /// <returns>JSON array with detailed information about all installed applications</returns>

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
    /// <summary>
    /// SYSTEM AGENT - PLAY MUSIC
    /// 
    /// Advanced music and media playback control system.
    /// Intelligently handles various music sources and playback methods:
    /// 
    /// MUSIC SOURCES:
    /// 🎵 Streaming Services: Spotify, YouTube Music, Apple Music
    /// 📁 Local Files: MP3, FLAC, WAV, AAC audio files
    /// 💿 CD/DVD: Physical media playback
    /// 📻 Radio: Internet radio stations
    /// 🎮 Game Audio: In-game music and soundtracks
    /// 
    /// PLAYBACK CONTROL:
    /// ▶️ Start Playback: Launch music apps and begin playing
    /// 🎯 Track Selection: Play specific songs or albums
    /// 📋 Playlist Management: Access saved playlists
    /// 🔀 Shuffle Mode: Random playback order
    /// 🔁 Repeat Options: Single track or playlist repeat
    /// 
    /// SMART FEATURES:
    /// 🧠 App Detection: Finds best available music app
    /// 🎤 Voice Commands: "Play my workout playlist"
    /// 🔍 Search Integration: Find songs by title or artist
    /// 📊 Context Awareness: Remembers previous music preferences
    /// 
    /// SUPPORTED PLAYERS:
    /// 🎧 Spotify: Premium and free accounts
    /// 🖥️ Windows Media Player: Built-in system player
    /// 🎬 VLC: Versatile multimedia player
    /// 🎵 iTunes/Apple Music: Apple ecosystem integration
    /// 🌐 Web Players: Browser-based music services
    /// 
    /// EXAMPLES:
    /// ✅ "Müzik çal" → Starts default music app
    /// ✅ "Spotify'da rock müzik aç" → Opens Spotify with rock music
    /// ✅ "Çalma listemi başlat" → Plays saved playlist
    /// </summary>
    /// <param name="trackName">Track name, playlist, or music service to play</param>
    /// <returns>JSON result with playback status, active player, and track information</returns>
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
    /// <summary>
    /// SYSTEM AGENT - CONTROL DEVICE
    /// 
    /// Comprehensive hardware device control and system management.
    /// Provides unified interface for controlling various system components:
    /// 
    /// DEVICE CATEGORIES:
    /// 🔊 Audio Devices: Volume, mute, speakers, microphones
    /// 📶 Network Hardware: WiFi adapters, Bluetooth, Ethernet
    /// 🔆 Display Systems: Brightness, multiple monitors, resolution
    /// ⚡ Power Management: Battery, charging, power modes
    /// 🖨️ Peripherals: Printers, scanners, external drives
    /// 🎮 Gaming Hardware: Controllers, headsets, RGB lighting
    /// 
    /// SUPPORTED ACTIONS:
    /// 🔛 Enable/Disable: Turn devices on or off
    /// 🎚️ Adjust Levels: Volume, brightness, performance
    /// 🔄 Toggle States: Switch between different modes
    /// ℹ️ Status Queries: Check current device states
    /// ⚙️ Configuration: Modify device settings
    /// 🔄 Reset/Restart: Reinitialize device connections
    /// 
    /// AUDIO CONTROL:
    /// 🔊 Volume: increase, decrease, set, mute, unmute
    /// 🎤 Microphone: enable, disable, adjust sensitivity
    /// 🔈 Speakers: switch output devices, balance
    /// 
    /// NETWORK CONTROL:
    /// 📶 WiFi: connect, disconnect, scan networks
    /// 🔵 Bluetooth: pair devices, disconnect, discover
    /// 🌐 Internet: connection status, speed test
    /// 
    /// DISPLAY CONTROL:
    /// 🔆 Brightness: increase, decrease, auto-adjust
    /// 🖥️ Monitors: extend, duplicate, switch primary
    /// 🎨 Color: adjust temperature, calibration
    /// 
    /// CONTEXT INTEGRATION:
    /// 📝 Remembers device preferences and settings
    /// 🧠 Learns usage patterns for smart automation
    /// ⚡ Provides instant status feedback
    /// 🔍 Troubleshoots common device issues
    /// 
    /// EXAMPLES:
    /// ✅ "Sesi aç" → Unmutes system audio
    /// ✅ "Bluetooth'u etkinleştir" → Turns on Bluetooth
    /// ✅ "Ekranı daha parlak yap" → Increases brightness
    /// ✅ "WiFi'ı kapat" → Disables wireless connection
    /// </summary>
    /// <param name="deviceName">Device to control (volume, bluetooth, wifi, brightness, etc.)</param>
    /// <param name="action">Action to perform (enable, disable, increase, decrease, toggle, status)</param>
    /// <returns>JSON result with device control status, current settings, and operation feedback</returns>

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
