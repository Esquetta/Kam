using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartVoiceAgent.Application.DependencyInjection;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.DependencyInjection;
using SmartVoiceAgent.Infrastructure.Extensions;

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
    })
    .Build();

// Test mode selector
Console.WriteLine("Select test mode:");
Console.WriteLine("1. Music Service Test (Play/Pause/Stop/Volume)");
Console.WriteLine("2. Run Voice Agent Host (default)");
Console.WriteLine();
Console.Write("Enter choice (1-2) [2]: ");

var choice = Console.ReadLine()?.Trim() ?? "2";

if (choice == "1")
{
    await RunMusicServiceTestAsync(host.Services);
}
else
{
    Console.WriteLine("\nStarting Voice Agent Host...");
    await host.RunAsync();
}

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
                        Console.Write("Enter audio file path: ");
                        argument = Console.ReadLine()?.Trim();
                    }
                    
                    if (!string.IsNullOrWhiteSpace(argument))
                    {
                        if (!File.Exists(argument))
                        {
                            Console.WriteLine($"❌ File not found: {argument}");
                            continue;
                        }
                        
                        currentFilePath = argument;
                        Console.WriteLine($"▶️ Playing: {Path.GetFileName(currentFilePath)}");
                        await musicService.PlayMusicAsync(currentFilePath, loop: false);
                        Console.WriteLine("✅ Playback started");
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
                        if (!File.Exists(argument))
                        {
                            Console.WriteLine($"❌ File not found: {argument}");
                            continue;
                        }
                        
                        currentFilePath = argument;
                        Console.WriteLine($"🔁 Playing in loop: {Path.GetFileName(currentFilePath)}");
                        await musicService.PlayMusicAsync(currentFilePath, loop: true);
                        Console.WriteLine("✅ Looped playback started");
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
                    await RunQuickTestAsync(musicService);
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

async Task RunQuickTestAsync(IMusicService musicService)
{
    Console.WriteLine("\n─── Quick Test Sequence ───");
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
    
    Console.WriteLine("─── Quick Test Complete ───\n");
}

string GetPlatformName()
{
    if (OperatingSystem.IsWindows()) return "Windows";
    if (OperatingSystem.IsLinux()) return "Linux";
    if (OperatingSystem.IsMacOS()) return "macOS";
    return "Unknown";
}
