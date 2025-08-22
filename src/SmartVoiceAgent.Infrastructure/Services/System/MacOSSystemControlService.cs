using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Infrastructure.Services.System;
public class MacOSSystemControlService : ISystemControlService
{
    public async Task<bool> SetSystemVolumeAsync(int level)
    {
        try
        {
            level = Math.Clamp(level, 0, 100);
            var result = await ExecuteShellCommandAsync($"osascript -e 'set volume output volume {level}'");
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IncreaseSystemVolumeAsync(int step = 10)
    {
        try
        {
            var currentVolume = await GetSystemVolumeAsync();
            var newVolume = Math.Min(currentVolume + step, 100);
            return await SetSystemVolumeAsync(newVolume);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DecreaseSystemVolumeAsync(int step = 10)
    {
        try
        {
            var currentVolume = await GetSystemVolumeAsync();
            var newVolume = Math.Max(currentVolume - step, 0);
            return await SetSystemVolumeAsync(newVolume);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> MuteSystemVolumeAsync()
    {
        try
        {
            var result = await ExecuteShellCommandAsync("osascript -e 'set volume output muted true'");
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UnmuteSystemVolumeAsync()
    {
        try
        {
            var result = await ExecuteShellCommandAsync("osascript -e 'set volume output muted false'");
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<int> GetSystemVolumeAsync()
    {
        try
        {
            var result = await ExecuteShellCommandAsync("osascript -e 'output volume of (get volume settings)'");
            if (result.Success && int.TryParse(result.Output.Trim(), out int volume))
            {
                return volume;
            }
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    public async Task<bool> SetScreenBrightnessAsync(int level)
    {
        try
        {
            level = Math.Clamp(level, 0, 100);
            double brightness = level / 100.0;
            var result = await ExecuteShellCommandAsync($"brightness {brightness:F2}");
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IncreaseScreenBrightnessAsync(int step = 10)
    {
        try
        {
            var currentBrightness = await GetScreenBrightnessAsync();
            var newBrightness = Math.Min(currentBrightness + step, 100);
            return await SetScreenBrightnessAsync(newBrightness);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DecreaseScreenBrightnessAsync(int step = 10)
    {
        try
        {
            var currentBrightness = await GetScreenBrightnessAsync();
            var newBrightness = Math.Max(currentBrightness - step, 0);
            return await SetScreenBrightnessAsync(newBrightness);
        }
        catch
        {
            return false;
        }
    }

    public async Task<int> GetScreenBrightnessAsync()
    {
        try
        {
            var result = await ExecuteShellCommandAsync("brightness -l");
            if (result.Success)
            {
                var match = Regex.Match(result.Output, @"brightness (\d+\.\d+)");
                if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double brightness))
                {
                    return (int)(brightness * 100);
                }
            }
            return 50; // Default value
        }
        catch
        {
            return 50;
        }
    }

    public async Task<bool> EnableWiFiAsync()
    {
        try
        {
            var result = await ExecuteShellCommandAsync("networksetup -setairportpower en0 on");
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DisableWiFiAsync()
    {
        try
        {
            var result = await ExecuteShellCommandAsync("networksetup -setairportpower en0 off");
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> GetWiFiStatusAsync()
    {
        try
        {
            var result = await ExecuteShellCommandAsync("networksetup -getairportpower en0");
            return result.Success && result.Output.Contains("On");
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> EnableBluetoothAsync()
    {
        try
        {
            var result = await ExecuteShellCommandAsync("blueutil -p 1");
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DisableBluetoothAsync()
    {
        try
        {
            var result = await ExecuteShellCommandAsync("blueutil -p 0");
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> GetBluetoothStatusAsync()
    {
        try
        {
            var result = await ExecuteShellCommandAsync("blueutil -p");
            return result.Success && result.Output.Trim() == "1";
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ShutdownSystemAsync(int delayMinutes = 0)
    {
        try
        {
            string command = delayMinutes > 0 ?
                $"sudo shutdown -h +{delayMinutes}" :
                "sudo shutdown -h now";
            var result = await ExecuteShellCommandAsync(command);
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RestartSystemAsync(int delayMinutes = 0)
    {
        try
        {
            string command = delayMinutes > 0 ?
                $"sudo shutdown -r +{delayMinutes}" :
                "sudo shutdown -r now";
            var result = await ExecuteShellCommandAsync(command);
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SleepSystemAsync()
    {
        try
        {
            var result = await ExecuteShellCommandAsync("pmset sleepnow");
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> LockSystemAsync()
    {
        try
        {
            var result = await ExecuteShellCommandAsync("/System/Library/CoreServices/Menu\\ Extras/User.menu/Contents/Resources/CGSession -suspend");
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<SystemStatusInfo> GetSystemStatusAsync()
    {
        var status = new SystemStatusInfo();

        try
        {
            // Volume
            status.VolumeLevel = await GetSystemVolumeAsync();
            var muteResult = await ExecuteShellCommandAsync("osascript -e 'output muted of (get volume settings)'");
            status.IsMuted = muteResult.Success && muteResult.Output.Trim().ToLower() == "true";

            // Brightness
            status.BrightnessLevel = await GetScreenBrightnessAsync();

            // WiFi
            status.IsWiFiEnabled = await GetWiFiStatusAsync();
            var wifiNetwork = await ExecuteShellCommandAsync("networksetup -getairportnetwork en0");
            status.WiFiStatus = wifiNetwork.Success ? wifiNetwork.Output.Trim() : "Unknown";

            // Bluetooth
            status.IsBluetoothEnabled = await GetBluetoothStatusAsync();
            status.BluetoothStatus = status.IsBluetoothEnabled ? "On" : "Off";

            // Battery
            var batteryResult = await ExecuteShellCommandAsync("pmset -g batt");
            if (batteryResult.Success)
            {
                var batteryMatch = Regex.Match(batteryResult.Output, @"(\d+)%");
                if (batteryMatch.Success && int.TryParse(batteryMatch.Groups[1].Value, out int batteryLevel))
                {
                    status.BatteryLevel = batteryLevel;
                }
                status.IsCharging = batteryResult.Output.Contains("AC Power");
                status.PowerStatus = status.IsCharging ? "Charging" : "Battery";
            }

            // CPU Usage
            var cpuResult = await ExecuteShellCommandAsync("top -l 1 -n 0 | grep 'CPU usage'");
            if (cpuResult.Success)
            {
                var cpuMatch = Regex.Match(cpuResult.Output, @"(\d+\.\d+)% user");
                if (cpuMatch.Success && double.TryParse(cpuMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double cpuUsage))
                {
                    status.CpuUsage = cpuUsage;
                }
            }

            // Memory Usage
            var memResult = await ExecuteShellCommandAsync("vm_stat");
            if (memResult.Success)
            {
                var pageSize = 4096; // Default page size on macOS
                var lines = memResult.Output.Split('\n');

                long pagesUsed = 0;
                long totalPages = 0;

                foreach (var line in lines)
                {
                    if (line.Contains("Pages free:"))
                    {
                        var match = Regex.Match(line, @"(\d+)");
                        if (match.Success && long.TryParse(match.Groups[1].Value, out long freePages))
                        {
                            totalPages += freePages;
                        }
                    }
                    else if (line.Contains("Pages active:") || line.Contains("Pages inactive:") || line.Contains("Pages wired down:"))
                    {
                        var match = Regex.Match(line, @"(\d+)");
                        if (match.Success && long.TryParse(match.Groups[1].Value, out long pages))
                        {
                            pagesUsed += pages;
                            totalPages += pages;
                        }
                    }
                }

                status.MemoryUsage = pagesUsed * pageSize;
                status.TotalMemory = totalPages * pageSize;
            }

            status.LastUpdated = DateTime.Now;
        }
        catch
        {
            // Return partial status even if some operations fail
        }

        return status;
    }

    private async Task<(bool Success, string Output)> ExecuteShellCommandAsync(string command)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
                return (false, string.Empty);

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            return (process.ExitCode == 0, string.IsNullOrEmpty(error) ? output : error);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}