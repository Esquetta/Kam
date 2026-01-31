#region Using Statements
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Application.DependencyInjection;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.DependencyInjection;
using SmartVoiceAgent.Infrastructure.Extensions;
using SmartVoiceAgent.Mailing.Entities;
using SmartVoiceAgent.Mailing.Interfaces;
using SmartVoiceAgent.Mailing.Extensions;
using MediatR;
#endregion

#region Main Entry Point
Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║         KAM Neural Core - Console Test Application        ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(config =>
    {
        config.AddUserSecrets<Program>();
    })
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;
        services.AddApplicationServices();
        services.AddSmartVoiceAgent(configuration);
        services.AddInfrastructureServices(configuration);
        
        // Add mailing services (Email and SMS)
        services.AddMailingServices(configuration);
    })
    .Build();

// Test mode selector
Console.WriteLine("Select test mode:");
Console.WriteLine("1. Music Service Test (Play/Pause/Stop/Volume)");
Console.WriteLine("2. Message Service Test (Send Email/SMS)");
Console.WriteLine("3. Device Control Test (Volume/WiFi/Bluetooth/Brightness/Power)");
Console.WriteLine("4. Application Scanner Test (List/Find installed apps)");
Console.WriteLine("5. Voice Recognition Test (Wake Word/Multi-STT/Noise Suppression)");
Console.WriteLine("6. Run Voice Agent Host (default)");
Console.WriteLine();
Console.Write("Enter choice (1-6) [6]: ");

var choice = Console.ReadLine()?.Trim() ?? "6";

switch (choice)
{
    case "1":
        await RunMusicServiceTestAsync(host.Services);
        break;
    case "2":
        await RunMessageServiceTestAsync(host.Services);
        break;
    case "3":
        await RunDeviceControlTestAsync(host.Services);
        break;
    case "4":
        await RunApplicationScannerTestAsync(host.Services);
        break;
    case "5":
        await RunVoiceRecognitionTestAsync(host.Services);
        break;
    default:
        Console.WriteLine("\nStarting Voice Agent Host...");
        await host.RunAsync();
        break;
}
#endregion

#region Music Service Test
async Task RunMusicServiceTestAsync(IServiceProvider services)
{
    Console.WriteLine("\n═══════════════════════════════════════════════════════════");
    Console.WriteLine("                 MUSIC SERVICE TEST MODE                   ");
    Console.WriteLine("═══════════════════════════════════════════════════════════\n");

    var musicService = services.GetRequiredService<IMusicService>();
    var serviceType = musicService.GetType().Name;
    
    Console.WriteLine($"Platform: {GetPlatformName()}");
    Console.WriteLine($"Service Implementation: {serviceType}");
    Console.WriteLine();
    Console.WriteLine("Supported audio formats: MP3, WAV, FLAC, OGG, AAC, M4A, WMA");
    Console.WriteLine();
    Console.WriteLine("💡 Tip: You can provide either:");
    Console.WriteLine("   - Full path: C:\\Music\\song.mp3");
    Console.WriteLine("   - Just filename: song (will search in Music folder and subfolders)");
    Console.WriteLine();

    string? currentFilePath = null;
    bool running = true;

    while (running)
    {
        Console.WriteLine("\n┌─────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Commands:                                               │");
        Console.WriteLine("│   [P]lay <name/path> - Play audio file (name or path)   │");
        Console.WriteLine("│   [PA]use            - Pause playback                   │");
        Console.WriteLine("│   [R]esume           - Resume playback                  │");
        Console.WriteLine("│   [S]top             - Stop playback                    │");
        Console.WriteLine("│   [V]olume <0-100>   - Set volume percentage            │");
        Console.WriteLine("│   [L]oop <name/path> - Play audio file in loop          │");
        Console.WriteLine("│   [I]nfo             - Show current status              │");
        Console.WriteLine("│   [T]est             - Quick test with sample commands  │");
        Console.WriteLine("│   [Q]uit             - Exit test mode                   │");
        Console.WriteLine("└─────────────────────────────────────────────────────────┘");
        Console.Write("\nEnter command: ");
        
        var input = Console.ReadLine()?.Trim() ?? "";
        var parts = input.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToUpperInvariant();
        var argument = parts.Length > 1 ? parts[1] : null;

        try
        {
            switch (command)
            {
                case "P":
                case "PLAY":
                    if (string.IsNullOrWhiteSpace(argument))
                    {
                        Console.Write("Enter audio file name or path: ");
                        argument = Console.ReadLine()?.Trim();
                    }
                    
                    if (!string.IsNullOrWhiteSpace(argument))
                    {
                        try
                        {
                            Console.WriteLine($"🔍 Looking for: {argument}");
                            currentFilePath = argument;
                            await musicService.PlayMusicAsync(currentFilePath, loop: false);
                            Console.WriteLine($"✅ Now playing: {Path.GetFileName(currentFilePath)}");
                        }
                        catch (FileNotFoundException)
                        {
                            Console.WriteLine($"❌ File not found: {argument}");
                            Console.WriteLine("💡 Make sure the file is in your Music folder or provide the full path.");
                        }
                    }
                    break;

                case "L":
                case "LOOP":
                    if (string.IsNullOrWhiteSpace(argument))
                    {
                        Console.Write("Enter audio file path: ");
                        argument = Console.ReadLine()?.Trim();
                    }
                    
                    if (!string.IsNullOrWhiteSpace(argument))
                    {
                        try
                        {
                            Console.WriteLine($"🔍 Looking for: {argument}");
                            currentFilePath = argument;
                            await musicService.PlayMusicAsync(currentFilePath, loop: true);
                            Console.WriteLine($"🔁 Looped playback started: {Path.GetFileName(currentFilePath)}");
                        }
                        catch (FileNotFoundException)
                        {
                            Console.WriteLine($"❌ File not found: {argument}");
                        }
                    }
                    break;

                case "PA":
                case "PAUSE":
                    Console.WriteLine("⏸️ Pausing...");
                    await musicService.PauseMusicAsync();
                    Console.WriteLine("✅ Paused");
                    break;

                case "R":
                case "RESUME":
                    Console.WriteLine("▶️ Resuming...");
                    await musicService.ResumeMusicAsync();
                    Console.WriteLine("✅ Resumed");
                    break;

                case "S":
                case "STOP":
                    Console.WriteLine("⏹️ Stopping...");
                    await musicService.StopMusicAsync();
                    Console.WriteLine("✅ Stopped");
                    break;

                case "V":
                case "VOLUME":
                    if (string.IsNullOrWhiteSpace(argument))
                    {
                        Console.Write("Enter volume (0-100): ");
                        argument = Console.ReadLine()?.Trim();
                    }
                    
                    if (float.TryParse(argument, out var volumePercent))
                    {
                        var volume = Math.Clamp(volumePercent / 100f, 0f, 1f);
                        Console.WriteLine($"🔊 Setting volume to {volumePercent}%...");
                        await musicService.SetVolumeAsync(volume);
                        Console.WriteLine("✅ Volume set");
                    }
                    else
                    {
                        Console.WriteLine("❌ Invalid volume. Use a number between 0-100.");
                    }
                    break;

                case "I":
                case "INFO":
                    Console.WriteLine("\n┌─ Current Status ───────────────────────────────────────┐");
                    Console.WriteLine($"│ Platform:     {GetPlatformName(),-40} │");
                    Console.WriteLine($"│ Service:      {serviceType,-40} │");
                    Console.WriteLine($"│ Last File:    {(currentFilePath ?? "None"),-40} │");
                    Console.WriteLine("└────────────────────────────────────────────────────────┘");
                    break;

                case "T":
                case "TEST":
                    await RunQuickMusicTestAsync(musicService);
                    break;

                case "Q":
                case "QUIT":
                case "EXIT":
                    running = false;
                    Console.WriteLine("\n⏹️ Stopping playback and exiting...");
                    await musicService.StopMusicAsync();
                    (musicService as IDisposable)?.Dispose();
                    Console.WriteLine("✅ Test mode ended.");
                    break;

                default:
                    Console.WriteLine("❌ Unknown command. Type 'P', 'PA', 'R', 'S', 'V', 'L', 'I', 'T', or 'Q'.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner: {ex.InnerException.Message}");
            }
        }
    }
}

async Task RunQuickMusicTestAsync(IMusicService musicService)
{
    Console.WriteLine("\n─── Quick Music Test Sequence ───");
    Console.WriteLine("This test will verify all music service operations.");
    Console.WriteLine("Note: Audio playback tests require a valid audio file.");
    Console.WriteLine();
    
    // Test 1: Set Volume
    Console.WriteLine("Test 1: Setting volume to 50%...");
    await musicService.SetVolumeAsync(0.5f);
    Console.WriteLine("✅ Volume test passed\n");
    
    // Test 2: Stop when not playing
    Console.WriteLine("Test 2: Stopping when not playing...");
    await musicService.StopMusicAsync();
    Console.WriteLine("✅ Stop test passed\n");
    
    // Test 3: Pause when not playing
    Console.WriteLine("Test 3: Pausing when not playing...");
    await musicService.PauseMusicAsync();
    Console.WriteLine("✅ Pause test passed\n");
    
    // Test 4: Resume when not playing
    Console.WriteLine("Test 4: Resuming when not playing...");
    await musicService.ResumeMusicAsync();
    Console.WriteLine("✅ Resume test passed\n");
    
    // Test 5: Ask for file to test actual playback
    Console.WriteLine("Test 5: Playback test (optional)");
    Console.Write("Enter path to a test audio file (or press Enter to skip): ");
    var testFile = Console.ReadLine()?.Trim();
    
    if (!string.IsNullOrWhiteSpace(testFile) && File.Exists(testFile))
    {
        try
        {
            Console.WriteLine($"Playing: {Path.GetFileName(testFile)} for 3 seconds...");
            await musicService.PlayMusicAsync(testFile, loop: false);
            await Task.Delay(3000);
            
            Console.WriteLine("Pausing...");
            await musicService.PauseMusicAsync();
            await Task.Delay(1000);
            
            Console.WriteLine("Resuming...");
            await musicService.ResumeMusicAsync();
            await Task.Delay(2000);
            
            Console.WriteLine("Stopping...");
            await musicService.StopMusicAsync();
            Console.WriteLine("✅ Playback test passed\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Playback test failed: {ex.Message}\n");
        }
    }
    else
    {
        Console.WriteLine("⏭️ Skipped playback test (no file provided)\n");
    }
    
    Console.WriteLine("─── Quick Music Test Complete ───\n");
}
#endregion

#region Message Service Test
async Task RunMessageServiceTestAsync(IServiceProvider services)
{
    Console.WriteLine("\n═══════════════════════════════════════════════════════════");
    Console.WriteLine("                MESSAGE SERVICE TEST MODE                  ");
    Console.WriteLine("═══════════════════════════════════════════════════════════\n");

    var mediator = services.GetRequiredService<IMediator>();
    var messageFactory = services.GetRequiredService<IMessageServiceFactory>();
    
    Console.WriteLine("Supported message types:");
    Console.WriteLine("  - Email: user@example.com");
    Console.WriteLine("  - More coming soon (SMS, Slack, etc.)");
    Console.WriteLine();
    Console.WriteLine("📧 Supported Email Providers:");
    Console.WriteLine("  • Gmail (App Password required - see SETUP_GUIDE.md)");
    Console.WriteLine("  • Outlook/Hotmail (App Password required)");
    Console.WriteLine("  • Yahoo Mail (App Password required)");
    Console.WriteLine("  • Office 365 (App Password required)");
    Console.WriteLine("  • SendGrid (API Key)");
    Console.WriteLine("  • Mailgun (SMTP credentials)");
    Console.WriteLine("  • Amazon SES (SMTP credentials)");
    Console.WriteLine();
    Console.WriteLine("💡 Note: Email requires SMTP configuration in appsettings.json");
    Console.WriteLine("   or User Secrets: dotnet user-secrets set \"Email:AppPassword\" \"your-password\"");
    Console.WriteLine();

    bool running = true;

    while (running)
    {
        Console.WriteLine("\n┌─────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Commands:                                               │");
        Console.WriteLine("│   [S]end <email> <message>  - Send message to recipient│");
        Console.WriteLine("│   [T]est                    - Run email validation test│");
        Console.WriteLine("│   [V]alidate <email>        - Check if email is valid  │");
        Console.WriteLine("│   [D]iagnose                - Show SMTP configuration  │");
        Console.WriteLine("│   [C]onnect                 - Test SMTP connection     │");
        Console.WriteLine("│   [Q]uit                    - Exit test mode           │");
        Console.WriteLine("└─────────────────────────────────────────────────────────┘");
        Console.Write("\nEnter command: ");
        
        var input = Console.ReadLine()?.Trim() ?? "";
        var parts = input.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToUpperInvariant();

        try
        {
            switch (command)
            {
                case "S":
                case "SEND":
                    await HandleSendMessageCommand(parts, mediator);
                    break;

                case "T":
                case "TEST":
                    await RunQuickMessageTestAsync(messageFactory);
                    break;

                case "V":
                case "VALIDATE":
                    if (parts.Length > 1)
                    {
                        var email = parts[1];
                        ValidateEmailAddress(email, messageFactory);
                    }
                    else
                    {
                        Console.Write("Enter email to validate: ");
                        var email = Console.ReadLine()?.Trim();
                        if (!string.IsNullOrWhiteSpace(email))
                        {
                            ValidateEmailAddress(email, messageFactory);
                        }
                    }
                    break;

                case "D":
                case "DIAGNOSE":
                    ShowSmtpDiagnostics(services);
                    break;

                case "C":
                case "CONNECT":
                    await TestSmtpConnectionAsync(services);
                    break;

                case "Q":
                case "QUIT":
                case "EXIT":
                    running = false;
                    Console.WriteLine("\n✅ Message service test ended.");
                    break;

                default:
                    Console.WriteLine("❌ Unknown command. Type 'S', 'T', 'V', 'D', 'C', or 'Q'.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner: {ex.InnerException.Message}");
            }
        }
    }
}

async Task HandleSendMessageCommand(string[] parts, IMediator mediator)
{
    string recipient;
    string message;

    if (parts.Length >= 3)
    {
        recipient = parts[1];
        message = parts[2];
    }
    else
    {
        Console.Write("Enter recipient (email): ");
        recipient = Console.ReadLine()?.Trim() ?? "";
        
        if (string.IsNullOrWhiteSpace(recipient))
        {
            Console.WriteLine("❌ Recipient cannot be empty.");
            return;
        }

        Console.Write("Enter message: ");
        message = Console.ReadLine()?.Trim() ?? "";
        
        if (string.IsNullOrWhiteSpace(message))
        {
            Console.WriteLine("❌ Message cannot be empty.");
            return;
        }
    }

    Console.WriteLine($"\n📤 Sending message to: {recipient}");
    Console.WriteLine($"   Content: {message}");
    
    try
    {
        var command = new SendMessageCommand(recipient, message);
        var result = await mediator.Send(command);
        
        if (result.Success)
        {
            Console.WriteLine($"✅ {result.Message}");
        }
        else
        {
            Console.WriteLine($"❌ {result.Message}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Failed to send: {ex.Message}");
    }
}

void ValidateEmailAddress(string email, IMessageServiceFactory factory)
{
    Console.WriteLine($"\n🔍 Validating: {email}");
    
    try
    {
        var service = factory.GetService(email);
        var serviceType = service.GetType().Name;
        Console.WriteLine($"✅ Valid email format!");
        Console.WriteLine($"   Handler: {serviceType}");
    }
    catch (NotSupportedException)
    {
        Console.WriteLine($"❌ Invalid email format or unsupported recipient type.");
    }
    catch (ArgumentException ex)
    {
        Console.WriteLine($"❌ {ex.Message}");
    }
}

async Task RunQuickMessageTestAsync(IMessageServiceFactory messageFactory)
{
    Console.WriteLine("\n─── Quick Message Test Sequence ───");
    Console.WriteLine("Testing email validation...\n");
    
    var testEmails = new[]
    {
        ("test@example.com", true),
        ("user.name@domain.co.uk", true),
        ("user+tag@example.com", true),
        ("invalid-email", false),
        ("@example.com", false),
        ("user@", false),
        ("", false)
    };
    
    int passed = 0;
    int failed = 0;
    
    foreach (var (email, shouldBeValid) in testEmails)
    {
        if (string.IsNullOrEmpty(email))
        {
            Console.WriteLine("Test: (empty string)");
        }
        else
        {
            Console.WriteLine($"Test: {email}");
        }
        
        try
        {
            var service = messageFactory.GetService(email);
            var isValid = true;
            
            if (isValid == shouldBeValid)
            {
                Console.WriteLine($"✅ PASS - Detected as {(isValid ? "valid" : "invalid")}\n");
                passed++;
            }
            else
            {
                Console.WriteLine($"❌ FAIL - Expected {(shouldBeValid ? "valid" : "invalid")}, got {(isValid ? "valid" : "invalid")}\n");
                failed++;
            }
        }
        catch (ArgumentException)
        {
            if (!shouldBeValid)
            {
                Console.WriteLine($"✅ PASS - Correctly rejected as invalid\n");
                passed++;
            }
            else
            {
                Console.WriteLine($"❌ FAIL - Should be valid but was rejected\n");
                failed++;
            }
        }
        catch (NotSupportedException)
        {
            if (!shouldBeValid)
            {
                Console.WriteLine($"✅ PASS - Correctly rejected as invalid\n");
                passed++;
            }
            else
            {
                Console.WriteLine($"❌ FAIL - Should be valid but was rejected\n");
                failed++;
            }
        }
    }
    
    Console.WriteLine($"─── Results: {passed} passed, {failed} failed ───\n");
}

void ShowSmtpDiagnostics(IServiceProvider services)
{
    try
    {
        var settingsOptions = services.GetService<IOptions<SmtpSettings>>();
        
        if (settingsOptions?.Value == null)
        {
            Console.WriteLine("\n❌ SMTP settings not configured!");
            Console.WriteLine("   Run: dotnet user-secrets set \"Email:Provider\" \"Gmail\"");
            Console.WriteLine("   Run: dotnet user-secrets set \"Email:Username\" \"your-email@gmail.com\"");
            Console.WriteLine("   Run: dotnet user-secrets set \"Email:AppPassword\" \"your-app-password\"");
            return;
        }
        
        var settings = settingsOptions.Value;
        settings.ApplyProviderDefaults();
        
        Console.WriteLine("\n┌─ SMTP Configuration Diagnostics ───────────────────────┐");
        Console.WriteLine($"│ Provider:        {settings.Provider,-40} │");
        Console.WriteLine($"│ Host:            {settings.Host,-40} │");
        Console.WriteLine($"│ Port:            {settings.Port,-40} │");
        Console.WriteLine($"│ EnableSsl:       {settings.EnableSsl,-40} │");
        Console.WriteLine($"│ UseStartTls:     {settings.UseStartTls,-40} │");
        Console.WriteLine($"│ AuthMethod:      {settings.AuthMethod,-40} │");
        Console.WriteLine($"│ Username:        {(settings.Username ?? "NOT SET"),-40} │");
        Console.WriteLine($"│ Password Set:    {(!string.IsNullOrEmpty(settings.Password) ? "YES" : "NO"),-40} │");
        Console.WriteLine($"│ AppPassword Set: {(!string.IsNullOrEmpty(settings.AppPassword) ? "YES" : "NO"),-40} │");
        Console.WriteLine($"│ FromAddress:     {(settings.FromAddress ?? "NOT SET"),-40} │");
        Console.WriteLine($"│ FromName:        {(settings.FromName ?? "NOT SET"),-40} │");
        Console.WriteLine($"│ SkipAuth:        {settings.SkipAuthentication,-40} │");
        Console.WriteLine("└────────────────────────────────────────────────────────┘");
        
        // Check for common issues
        var issues = new List<string>();
        
        if (string.IsNullOrEmpty(settings.Username))
            issues.Add("❌ Username/Email is not set");
        
        if (!settings.SkipAuthentication && 
            string.IsNullOrEmpty(settings.AppPassword) && 
            string.IsNullOrEmpty(settings.Password))
            issues.Add("❌ No password or AppPassword configured");
        
        if (settings.Provider == SmtpProvider.Gmail && 
            string.IsNullOrEmpty(settings.AppPassword) && 
            !string.IsNullOrEmpty(settings.Password))
            issues.Add("⚠️ Gmail requires App Password, not regular password");
        
        if (string.IsNullOrEmpty(settings.FromAddress))
            issues.Add("❌ From address is not set");
        
        if (!settings.EnableSsl && !settings.UseStartTls)
            issues.Add("⚠️ SSL/TLS is disabled - most providers require it");
        
        if (issues.Count > 0)
        {
            Console.WriteLine("\n⚠️ Configuration Issues Found:");
            foreach (var issue in issues)
            {
                Console.WriteLine($"   {issue}");
            }
        }
        else
        {
            Console.WriteLine("\n✅ Basic configuration looks good!");
        }
        
        Console.WriteLine("\n💡 To fix configuration:");
        Console.WriteLine("   dotnet user-secrets set \"Email:Provider\" \"Gmail\"");
        Console.WriteLine("   dotnet user-secrets set \"Email:Username\" \"your-email@gmail.com\"");
        Console.WriteLine("   dotnet user-secrets set \"Email:AppPassword\" \"your-16-char-app-password\"");
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n❌ Error reading configuration: {ex.Message}");
    }
}

async Task TestSmtpConnectionAsync(IServiceProvider services)
{
    Console.WriteLine("\n🔌 Testing SMTP connection...");
    
    try
    {
        var emailService = services.GetService<IEmailService>();
        
        if (emailService == null)
        {
            Console.WriteLine("❌ Email service not available!");
            return;
        }
        
        var result = await emailService.TestConnectionAsync();
        
        if (result)
        {
            Console.WriteLine("✅ SMTP connection successful!");
            Console.WriteLine("   Connected and authenticated successfully.");
        }
        else
        {
            Console.WriteLine("❌ SMTP connection failed!");
            Console.WriteLine("   Check your credentials and try again.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Connection test failed: {ex.Message}");
        if (ex.InnerException != null)
        {
            Console.WriteLine($"   Details: {ex.InnerException.Message}");
        }
    }
    
    Console.WriteLine();
}
#endregion

#region Device Control Test
async Task RunDeviceControlTestAsync(IServiceProvider services)
{
    Console.WriteLine("\n═══════════════════════════════════════════════════════════");
    Console.WriteLine("               DEVICE CONTROL TEST MODE                    ");
    Console.WriteLine("═══════════════════════════════════════════════════════════\n");

    var mediator = services.GetRequiredService<IMediator>();
    var factory = services.GetRequiredService<ISystemControlServiceFactory>();
    var systemControl = factory.CreateSystemService();
    
    Console.WriteLine($"Platform: {GetPlatformName()}");
    Console.WriteLine($"Service Implementation: {systemControl.GetType().Name}");
    Console.WriteLine();
    Console.WriteLine("Supported devices:");
    Console.WriteLine("  • Volume: increase, decrease, mute, unmute");
    Console.WriteLine("  • Brightness: increase, decrease");
    Console.WriteLine("  • WiFi: on, off, status");
    Console.WriteLine("  • Bluetooth: on, off, status");
    Console.WriteLine("  • Power: shutdown, restart, sleep, lock, status");
    Console.WriteLine();

    bool running = true;

    while (running)
    {
        Console.WriteLine("\n┌─────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Commands:                                               │");
        Console.WriteLine("│   [V]olume <action>       - Control volume              │");
        Console.WriteLine("│   [B]rightness <action>   - Control screen brightness   │");
        Console.WriteLine("│   [W]iFi <action>         - Control WiFi                │");
        Console.WriteLine("│   BL[T]ooth <action>      - Control Bluetooth           │");
        Console.WriteLine("│   [P]ower <action>        - Control power (shutdown...) │");
        Console.WriteLine("│   [S]tatus                - Show system status          │");
        Console.WriteLine("│   [Q]uit                  - Exit test mode              │");
        Console.WriteLine("└─────────────────────────────────────────────────────────┘");
        Console.Write("\nEnter command: ");
        
        var input = Console.ReadLine()?.Trim() ?? "";
        var parts = input.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToUpperInvariant();
        var argument = parts.Length > 1 ? parts[1].ToLowerInvariant() : null;

        try
        {
            switch (command)
            {
                case "V":
                case "VOLUME":
                    if (string.IsNullOrWhiteSpace(argument))
                    {
                        Console.Write("Enter action (increase/decrease/mute/unmute): ");
                        argument = Console.ReadLine()?.Trim().ToLowerInvariant();
                    }
                    await ExecuteDeviceCommandAsync(mediator, "volume", argument!);
                    break;

                case "B":
                case "BRIGHTNESS":
                    if (string.IsNullOrWhiteSpace(argument))
                    {
                        Console.Write("Enter action (increase/decrease): ");
                        argument = Console.ReadLine()?.Trim().ToLowerInvariant();
                    }
                    await ExecuteDeviceCommandAsync(mediator, "brightness", argument!);
                    break;

                case "W":
                case "WIFI":
                    if (string.IsNullOrWhiteSpace(argument))
                    {
                        Console.Write("Enter action (on/off/status): ");
                        argument = Console.ReadLine()?.Trim().ToLowerInvariant();
                    }
                    await ExecuteDeviceCommandAsync(mediator, "wifi", argument!);
                    break;

                case "T":
                case "BLUETOOTH":
                    if (string.IsNullOrWhiteSpace(argument))
                    {
                        Console.Write("Enter action (on/off/status): ");
                        argument = Console.ReadLine()?.Trim().ToLowerInvariant();
                    }
                    await ExecuteDeviceCommandAsync(mediator, "bluetooth", argument!);
                    break;

                case "P":
                case "POWER":
                    if (string.IsNullOrWhiteSpace(argument))
                    {
                        Console.Write("Enter action (shutdown/restart/sleep/lock/status): ");
                        argument = Console.ReadLine()?.Trim().ToLowerInvariant();
                    }
                    // Confirm destructive actions
                    if (argument == "shutdown" || argument == "restart" || argument == "sleep" || argument == "kapat" || argument == "yeniden başlat")
                    {
                        Console.Write($"⚠️ Are you sure you want to {argument} the system? (yes/no): ");
                        var confirm = Console.ReadLine()?.Trim().ToLowerInvariant();
                        if (confirm != "yes" && confirm != "y")
                        {
                            Console.WriteLine("Cancelled.");
                            break;
                        }
                    }
                    await ExecuteDeviceCommandAsync(mediator, "power", argument!);
                    break;

                case "S":
                case "STATUS":
                    await ExecuteDeviceCommandAsync(mediator, "power", "status");
                    break;

                case "Q":
                case "QUIT":
                case "EXIT":
                    running = false;
                    Console.WriteLine("\n✅ Device control test ended.");
                    break;

                default:
                    Console.WriteLine("❌ Unknown command. Type 'V', 'B', 'W', 'T', 'P', 'S', or 'Q'.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner: {ex.InnerException.Message}");
            }
        }
    }
}

async Task ExecuteDeviceCommandAsync(IMediator mediator, string device, string action)
{
    if (string.IsNullOrWhiteSpace(action))
    {
        Console.WriteLine("❌ Action cannot be empty.");
        return;
    }

    Console.WriteLine($"\n🎛️ Executing: {action} on {device}...");
    
    try
    {
        var command = new ControlDeviceCommand(device, action);
        var result = await mediator.Send(command);
        
        if (result.Success)
        {
            Console.WriteLine($"✅ {result.Message}");
        }
        else
        {
            Console.WriteLine($"❌ {result.Message}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Failed to execute command: {ex.Message}");
    }
}
#endregion

#region Application Scanner Test
async Task RunApplicationScannerTestAsync(IServiceProvider services)
{
    Console.WriteLine("\n═══════════════════════════════════════════════════════════");
    Console.WriteLine("              APPLICATION SCANNER TEST MODE                ");
    Console.WriteLine("═══════════════════════════════════════════════════════════\n");

    var mediator = services.GetRequiredService<IMediator>();
    var scannerFactory = services.GetRequiredService<IApplicationScannerServiceFactory>();
    var scanner = scannerFactory.Create();
    
    Console.WriteLine($"Platform: {GetPlatformName()}");
    Console.WriteLine($"Scanner Implementation: {scanner.GetType().Name}");
    Console.WriteLine();

    bool running = true;

    while (running)
    {
        Console.WriteLine("\n┌─────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Commands:                                               │");
        Console.WriteLine("│   [L]ist [system]         - List installed applications│");
        Console.WriteLine("│   [F]ind <app name>       - Find specific application  │");
        Console.WriteLine("│   [P]ath <app name>       - Get executable path        │");
        Console.WriteLine("│   [S]can quick            - Quick scan (top 20 apps)   │");
        Console.WriteLine("│   [Q]uit                  - Exit test mode             │");
        Console.WriteLine("└─────────────────────────────────────────────────────────┘");
        Console.Write("\nEnter command: ");
        
        var input = Console.ReadLine()?.Trim() ?? "";
        var parts = input.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToUpperInvariant();
        var argument = parts.Length > 1 ? parts[1] : null;

        try
        {
            switch (command)
            {
                case "L":
                case "LIST":
                    await ListApplicationsAsync(mediator, argument);
                    break;

                case "F":
                case "FIND":
                    if (string.IsNullOrWhiteSpace(argument))
                    {
                        Console.Write("Enter application name to find: ");
                        argument = Console.ReadLine()?.Trim();
                    }
                    if (!string.IsNullOrWhiteSpace(argument))
                    {
                        await FindApplicationAsync(scanner, argument);
                    }
                    break;

                case "P":
                case "PATH":
                    if (string.IsNullOrWhiteSpace(argument))
                    {
                        Console.Write("Enter application name to get path: ");
                        argument = Console.ReadLine()?.Trim();
                    }
                    if (!string.IsNullOrWhiteSpace(argument))
                    {
                        await GetApplicationPathAsync(scanner, argument);
                    }
                    break;

                case "S":
                case "SCAN":
                    await QuickScanAsync(scanner);
                    break;

                case "Q":
                case "QUIT":
                case "EXIT":
                    running = false;
                    Console.WriteLine("\n✅ Application scanner test ended.");
                    break;

                default:
                    Console.WriteLine("❌ Unknown command. Type 'L', 'F', 'P', 'S', or 'Q'.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner: {ex.InnerException.Message}");
            }
        }
    }
}

async Task ListApplicationsAsync(IMediator mediator, string? argument)
{
    bool includeSystemApps = argument?.ToLower() == "system";
    
    Console.WriteLine($"\n🔍 Scanning for installed applications (System apps: {(includeSystemApps ? "included" : "excluded")})...");
    Console.WriteLine("This may take a few seconds...\n");
    
    try
    {
        var command = new ListInstalledApplicationsCommand(includeSystemApps);
        var result = await mediator.Send(command);
        
        if (result.Success)
        {
            Console.WriteLine($"✅ {result.Message}");
            if (result.Data != null)
            {
                var dataString = result.Data.ToString();
                if (!string.IsNullOrEmpty(dataString))
                {
                    // Pretty print the JSON
                    try
                    {
                        var apps = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(dataString);
                        Console.WriteLine("\n📋 Installed Applications:");
                        Console.WriteLine("─────────────────────────────────────────────────────────");
                        
                        int count = 0;
                        foreach (var app in apps.EnumerateArray())
                        {
                            count++;
                            var name = app.GetProperty("name").GetString();
                            var path = app.GetProperty("path").GetString();
                            var isRunning = app.GetProperty("isRunning").GetBoolean();
                            
                            var statusIcon = isRunning ? "🟢" : "⚪";
                            var truncatedPath = path?.Length > 50 ? path.Substring(0, 47) + "..." : path;
                            
                            Console.WriteLine($"{statusIcon} {count}. {name}");
                            if (!string.IsNullOrEmpty(truncatedPath))
                            {
                                Console.WriteLine($"   📁 {truncatedPath}");
                            }
                            
                            // Limit output to avoid console flooding
                            if (count >= 50 && apps.GetArrayLength() > 50)
                            {
                                Console.WriteLine($"\n... and {apps.GetArrayLength() - count} more applications");
                                break;
                            }
                        }
                        
                        Console.WriteLine("─────────────────────────────────────────────────────────");
                        Console.WriteLine($"📝 Total: {count} applications shown");
                        Console.WriteLine("🟢 = Running | ⚪ = Not running");
                    }
                    catch
                    {
                        // Fallback to raw JSON
                        Console.WriteLine(dataString);
                    }
                }
            }
        }
        else
        {
            Console.WriteLine($"❌ {result.Message}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Failed to list applications: {ex.Message}");
    }
}

async Task FindApplicationAsync(IApplicationScanner scanner, string appName)
{
    Console.WriteLine($"\n🔍 Searching for: {appName}...");
    
    try
    {
        var result = await scanner.FindApplicationAsync(appName);
        
        if (result.IsInstalled)
        {
            Console.WriteLine($"✅ Application found: {result.DisplayName}");
            Console.WriteLine($"   📁 Path: {result.ExecutablePath}");
            if (!string.IsNullOrEmpty(result.Version))
            {
                Console.WriteLine($"   📌 Version: {result.Version}");
            }
            if (result.InstallDate.HasValue)
            {
                Console.WriteLine($"   📅 Install Date: {result.InstallDate.Value:yyyy-MM-dd}");
            }
        }
        else
        {
            Console.WriteLine($"❌ Application '{appName}' not found.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error finding application: {ex.Message}");
    }
}

async Task GetApplicationPathAsync(IApplicationScanner scanner, string appName)
{
    Console.WriteLine($"\n🔍 Getting path for: {appName}...");
    
    try
    {
        var path = await scanner.GetApplicationPathAsync(appName);
        
        if (!string.IsNullOrEmpty(path))
        {
            Console.WriteLine($"✅ Path found: {path}");
            
            // Check if file exists
            if (File.Exists(path))
            {
                Console.WriteLine("   ✅ Executable file exists");
                
                var fileInfo = new FileInfo(path);
                Console.WriteLine($"   📊 Size: {fileInfo.Length / 1024 / 1024} MB");
                Console.WriteLine($"   📅 Last Modified: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm}");
            }
            else
            {
                Console.WriteLine("   ⚠️ Executable file not found at path");
            }
        }
        else
        {
            Console.WriteLine($"❌ Path not found for '{appName}'");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error getting path: {ex.Message}");
    }
}

async Task QuickScanAsync(IApplicationScanner scanner)
{
    Console.WriteLine("\n🔍 Quick scanning for installed applications...");
    Console.WriteLine("Showing first 20 applications:\n");
    
    try
    {
        var apps = await scanner.GetInstalledApplicationsAsync();
        var topApps = apps.Take(20).ToList();
        
        Console.WriteLine("─────────────────────────────────────────────────────────");
        int count = 0;
        foreach (var app in topApps)
        {
            count++;
            var statusIcon = app.IsRunning ? "🟢" : "⚪";
            Console.WriteLine($"{statusIcon} {count,2}. {app.Name}");
        }
        Console.WriteLine("─────────────────────────────────────────────────────────");
        Console.WriteLine($"📊 Showing {count} of {apps.Count()} applications");
        Console.WriteLine("🟢 = Running | ⚪ = Not running");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error during quick scan: {ex.Message}");
    }
}
#endregion

#region Voice Recognition Test
async Task RunVoiceRecognitionTestAsync(IServiceProvider services)
{
    Console.WriteLine("\n═══════════════════════════════════════════════════════════");
    Console.WriteLine("            VOICE RECOGNITION TEST MODE                    ");
    Console.WriteLine("═══════════════════════════════════════════════════════════\n");

    Console.WriteLine("Available Tests:");
    Console.WriteLine("  1. Wake Word Detection - Test keyword activation");
    Console.WriteLine("  2. Multi-STT Provider  - Test speech-to-text with fallback");
    Console.WriteLine("  3. Noise Suppression   - Test audio enhancement");
    Console.WriteLine("  4. Voice Recording     - Record and process audio");
    Console.WriteLine();

    bool running = true;

    while (running)
    {
        Console.WriteLine("\n┌─────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Commands:                                               │");
        Console.WriteLine("│   [W]ake               - Test wake word detection       │");
        Console.WriteLine("│   [S]TT                - Test STT with fallback         │");
        Console.WriteLine("│   [N]oise              - Test noise suppression         │");
        Console.WriteLine("│   [R]ecord             - Record and transcribe audio    │");
        Console.WriteLine("│   [H]ealth             - Check STT provider health      │");
        Console.WriteLine("│   [Q]uit               - Exit test mode                 │");
        Console.WriteLine("└─────────────────────────────────────────────────────────┘");
        Console.Write("\nEnter command: ");
        
        var input = Console.ReadLine()?.Trim() ?? "";
        var command = input.ToUpperInvariant();

        try
        {
            switch (command)
            {
                case "W":
                case "WAKE":
                    await TestWakeWordDetectionAsync(services);
                    break;

                case "S":
                case "STT":
                    await TestMultiSTTAsync(services);
                    break;

                case "N":
                case "NOISE":
                    await TestNoiseSuppressionAsync(services);
                    break;

                case "R":
                case "RECORD":
                    await TestVoiceRecordingAsync(services);
                    break;

                case "H":
                case "HEALTH":
                    await CheckSTTProviderHealthAsync(services);
                    break;

                case "Q":
                case "QUIT":
                case "EXIT":
                    running = false;
                    Console.WriteLine("\n✅ Voice recognition test ended.");
                    break;

                default:
                    Console.WriteLine("❌ Unknown command. Type 'W', 'S', 'N', 'R', 'H', or 'Q'.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner: {ex.InnerException.Message}");
            }
        }
    }
}

async Task TestWakeWordDetectionAsync(IServiceProvider services)
{
    Console.WriteLine("\n🎤 Wake Word Detection Test");
    Console.WriteLine("─────────────────────────────────────────────────────────");
    
    var wakeWordService = services.GetRequiredService<IWakeWordDetectionService>();
    
    Console.WriteLine($"Current wake word: '{wakeWordService.WakeWord}'");
    Console.WriteLine($"Sensitivity: {wakeWordService.Sensitivity:P0}");
    Console.WriteLine();
    
    using var cts = new CancellationTokenSource();
    var detectionTask = new TaskCompletionSource<bool>();
    
    wakeWordService.OnWakeWordDetected += (s, e) =>
    {
        Console.WriteLine($"\n🎯 WAKE WORD DETECTED!");
        Console.WriteLine($"   Word: {e.WakeWord}");
        Console.WriteLine($"   Confidence: {e.Confidence:P2}");
        Console.WriteLine($"   Time: {e.DetectedAt:HH:mm:ss.fff}");
        detectionTask.TrySetResult(true);
    };
    
    wakeWordService.OnError += (s, ex) =>
    {
        Console.WriteLine($"\n❌ Error: {ex.Message}");
        detectionTask.TrySetResult(false);
    };
    
    Console.WriteLine("Starting wake word detection...");
    Console.WriteLine($"Say '{wakeWordService.WakeWord}' to trigger detection.");
    Console.WriteLine("Press any key to stop listening...\n");
    
    wakeWordService.StartListening();
    
    // Wait for either detection or key press
    var keyPressTask = Task.Run(() => Console.ReadKey(true));
    var completedTask = await Task.WhenAny(detectionTask.Task, keyPressTask);
    
    wakeWordService.StopListening();
    
    if (completedTask == detectionTask.Task && detectionTask.Task.Result)
    {
        Console.WriteLine("\n✅ Wake word detection successful!");
    }
    else
    {
        Console.WriteLine("\n⏹️ Detection stopped by user.");
    }
}

async Task TestMultiSTTAsync(IServiceProvider services)
{
    Console.WriteLine("\n🗣️ Multi-STT Provider Test");
    Console.WriteLine("─────────────────────────────────────────────────────────");
    
    var multiSTT = services.GetRequiredService<IMultiSTTService>();
    
    Console.WriteLine("Testing STT providers with automatic fallback...");
    Console.WriteLine();
    
    // Check provider health first
    var healthStatus = multiSTT.GetProviderHealthStatus();
    Console.WriteLine("Provider Health Status:");
    foreach (var status in healthStatus)
    {
        var healthIcon = status.Value.IsHealthy ? "🟢" : "🔴";
        Console.WriteLine($"  {healthIcon} {status.Key}: {(status.Value.IsHealthy ? "Healthy" : "Unhealthy")} (Success rate: {status.Value.SuccessRate:P0})");
    }
    Console.WriteLine();
    
    // Simulate STT test with a warning
    Console.WriteLine("⚠️  Note: This test requires audio input.");
    Console.WriteLine("For a full test, use option [R] Record instead.");
    Console.WriteLine();
    Console.WriteLine("Testing connection to providers...");
    
    try
    {
        var results = await multiSTT.TestAllProvidersAsync();
        
        Console.WriteLine("\nConnection Test Results:");
        foreach (var result in results)
        {
            var icon = result.IsConnected ? "✅" : "❌";
            Console.WriteLine($"  {icon} {result.Provider}: {(result.IsConnected ? "Connected" : "Failed")} ({result.ResponseTime.TotalMilliseconds:F0}ms)");
            if (!result.IsConnected && !string.IsNullOrEmpty(result.ErrorMessage))
            {
                Console.WriteLine($"     Error: {result.ErrorMessage}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error testing providers: {ex.Message}");
    }
}

async Task TestNoiseSuppressionAsync(IServiceProvider services)
{
    Console.WriteLine("\n🔇 Noise Suppression Test");
    Console.WriteLine("─────────────────────────────────────────────────────────");
    
    var noiseSuppression = services.GetRequiredService<INoiseSuppressionService>();
    
    Console.WriteLine($"Service Initialized: {(noiseSuppression.IsInitialized ? "✅ Yes" : "❌ No")}");
    Console.WriteLine();
    
    // Generate a test audio signal with noise
    Console.WriteLine("Generating test audio signal...");
    var testAudio = GenerateTestAudioWithNoise();
    
    // Estimate noise level
    var noiseLevel = noiseSuppression.EstimateNoiseLevel(testAudio);
    Console.WriteLine($"Estimated noise level: {noiseLevel:P2}");
    
    // Apply noise suppression
    Console.WriteLine("Applying noise suppression...");
    var options = new NoiseSuppressionOptions
    {
        SuppressionStrength = 0.7f,
        ApplyAGC = true,
        ApplyHighPassFilter = true
    };
    
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var processedAudio = noiseSuppression.SuppressNoise(testAudio, options);
    stopwatch.Stop();
    
    var processedNoiseLevel = noiseSuppression.EstimateNoiseLevel(processedAudio);
    
    Console.WriteLine($"✅ Processing complete in {stopwatch.ElapsedMilliseconds}ms");
    Console.WriteLine($"   Original size: {testAudio.Length} bytes");
    Console.WriteLine($"   Processed size: {processedAudio.Length} bytes");
    Console.WriteLine($"   Noise reduction: {noiseLevel - processedNoiseLevel:P2}");
    Console.WriteLine();
    Console.WriteLine("Note: This is a simulated test with synthetic audio.");
    Console.WriteLine("For real audio testing, use option [R] Record.");
}

async Task TestVoiceRecordingAsync(IServiceProvider services)
{
    Console.WriteLine("\n🎙️ Voice Recording Test");
    Console.WriteLine("─────────────────────────────────────────────────────────");
    
    var voiceRecognitionFactory = services.GetRequiredService<IVoiceRecognitionFactory>();
    var voiceRecognition = voiceRecognitionFactory.Create();
    var multiSTT = services.GetRequiredService<IMultiSTTService>();
    var noiseSuppression = services.GetRequiredService<INoiseSuppressionService>();
    
    Console.WriteLine("This test will:");
    Console.WriteLine("  1. Record audio for 5 seconds");
    Console.WriteLine("  2. Apply noise suppression");
    Console.WriteLine("  3. Transcribe using Multi-STT with fallback");
    Console.WriteLine();
    
    Console.Write("Press Enter to start recording (or type 'skip' to cancel)...");
    var input = Console.ReadLine()?.Trim().ToLower();
    if (input == "skip")
    {
        Console.WriteLine("⏭️ Test skipped.");
        return;
    }
    
    try
    {
        var tcs = new TaskCompletionSource<byte[]>();
        
        voiceRecognition.OnVoiceCaptured += (s, data) =>
        {
            Console.WriteLine($"\n🎤 Audio captured: {data.Length} bytes");
            tcs.TrySetResult(data);
        };
        
        voiceRecognition.OnError += (s, ex) =>
        {
            Console.WriteLine($"\n❌ Recording error: {ex.Message}");
            tcs.TrySetException(ex);
        };
        
        Console.WriteLine("\n🔴 Recording... Speak now!");
        voiceRecognition.StartListening();
        
        // Record for 5 seconds or until voice is captured
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n⏹️ Recording timeout (5 seconds).");
            voiceRecognition.StopListening();
            return;
        }
        
        voiceRecognition.StopListening();
        
        var audioData = await tcs.Task;
        
        // Apply noise suppression
        Console.WriteLine("🔇 Applying noise suppression...");
        var cleanAudio = noiseSuppression.SuppressNoise(audioData);
        Console.WriteLine($"   Noise suppression: {audioData.Length} → {cleanAudio.Length} bytes");
        
        // Transcribe
        Console.WriteLine("📝 Transcribing audio...");
        var result = await multiSTT.ConvertToTextAsync(cleanAudio);
        
        Console.WriteLine("\n📊 Transcription Result:");
        Console.WriteLine($"   Text: {result.Text}");
        Console.WriteLine($"   Confidence: {result.Confidence:P2}");
        Console.WriteLine($"   Provider: {result.UsedProvider}");
        Console.WriteLine($"   Fallback used: {(result.WasFallbackUsed ? "Yes" : "No")}");
        if (result.ProvidersTried.Count > 0)
        {
            Console.WriteLine($"   Providers tried: {string.Join(", ", result.ProvidersTried)}");
        }
        Console.WriteLine($"   Processing time: {result.ProcessingTime.TotalMilliseconds:F0}ms");
        
        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            Console.WriteLine($"   Error: {result.ErrorMessage}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n❌ Test failed: {ex.Message}");
    }
}

async Task CheckSTTProviderHealthAsync(IServiceProvider services)
{
    Console.WriteLine("\n🏥 STT Provider Health Check");
    Console.WriteLine("─────────────────────────────────────────────────────────");
    
    var multiSTT = services.GetRequiredService<IMultiSTTService>();
    var healthStatus = multiSTT.GetProviderHealthStatus();
    
    Console.WriteLine("Current Provider Status:\n");
    
    foreach (var status in healthStatus)
    {
        var icon = status.Value.IsHealthy ? "🟢" : "🔴";
        Console.WriteLine($"{icon} {status.Key}");
        Console.WriteLine($"   Health: {(status.Value.IsHealthy ? "Healthy" : "Unhealthy")}");
        Console.WriteLine($"   Success Rate: {status.Value.SuccessRate:P1}");
        Console.WriteLine($"   Success Count: {status.Value.SuccessCount}");
        Console.WriteLine($"   Failure Count: {status.Value.FailureCount}");
        Console.WriteLine($"   Avg Response: {status.Value.AverageResponseTime.TotalMilliseconds:F0}ms");
        Console.WriteLine($"   Last Checked: {status.Value.LastChecked:HH:mm:ss}");
        if (!string.IsNullOrEmpty(status.Value.LastError))
        {
            Console.WriteLine($"   Last Error: {status.Value.LastError}");
        }
        Console.WriteLine();
    }
    
    Console.WriteLine("Press any key to test connections...");
    Console.ReadKey(true);
    
    Console.WriteLine("\nTesting connections...");
    var results = await multiSTT.TestAllProvidersAsync();
    
    Console.WriteLine("\nConnection Test Results:");
    foreach (var result in results)
    {
        var icon = result.IsConnected ? "✅" : "❌";
        Console.WriteLine($"  {icon} {result.Provider}: {(result.IsConnected ? "Connected" : "Failed")} ({result.ResponseTime.TotalMilliseconds:F0}ms)");
    }
}

byte[] GenerateTestAudioWithNoise()
{
    // Generate 1 second of 16-bit mono audio at 16kHz with synthetic speech + noise
    int sampleRate = 16000;
    int durationMs = 1000;
    int sampleCount = sampleRate * durationMs / 1000;
    var audio = new short[sampleCount];
    
    var random = new Random();
    
    for (int i = 0; i < sampleCount; i++)
    {
        // Add synthetic "speech" signal (sine wave)
        double time = (double)i / sampleRate;
        double speechSignal = Math.Sin(2 * Math.PI * 440 * time) * 5000; // 440 Hz tone
        
        // Add noise
        double noise = (random.NextDouble() - 0.5) * 2000;
        
        // Combine
        double sample = speechSignal + noise;
        audio[i] = (short)Math.Clamp(sample, short.MinValue, short.MaxValue);
    }
    
    // Convert to bytes
    var bytes = new byte[sampleCount * 2];
    Buffer.BlockCopy(audio, 0, bytes, 0, bytes.Length);
    return bytes;
}
#endregion

#region Helper Methods
string GetPlatformName()
{
    if (OperatingSystem.IsWindows()) return "Windows";
    if (OperatingSystem.IsLinux()) return "Linux";
    if (OperatingSystem.IsMacOS()) return "macOS";
    return "Unknown";
}
#endregion
