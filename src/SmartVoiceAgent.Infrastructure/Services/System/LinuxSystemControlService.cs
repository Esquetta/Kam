using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

public class LinuxSystemControlService : ISystemControlService
{
    public async Task<bool> SetSystemVolumeAsync(int level)
    {
        try
        {
            level = Math.Clamp(level, 0, 100);
            var result = await ExecuteShellCommandAsync($"amixer -D pulse sset Master {level}%");
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
            var result = await ExecuteShellCommandAsync($"amixer -D pulse sset Master {step}%+");
            return result.Success;
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
            var result = await ExecuteShellCommandAsync($"amixer -D pulse sset Master {step}%-");
            return result.Success;
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
            var result = await ExecuteShellCommandAsync("amixer -D pulse set Master mute");
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
            var result = await ExecuteShellCommandAsync("amixer -D pulse set Master unmute");
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
            var result = await ExecuteShellCommandAsync("amixer -D pulse get Master");
            if (result.Success)
            {
                var match = Regex.Match(result.Output, @"\[(\d+)%\]");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int volume))
                {
                    return volume;
                }
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

            // Try different brightness control methods
            var brightnessPath = await GetBrightnessControlPathAsync();
            if (!string.IsNullOrEmpty(brightnessPath))
            {
                var maxBrightnessPath = Path.Combine(Path.GetDirectoryName(brightnessPath), "max_brightness");
                if (File.Exists(maxBrightnessPath))
                {
                    var maxBrightnessStr = await File.ReadAllTextAsync(maxBrightnessPath);
                    if (int.TryParse(maxBrightnessStr.Trim(), out int maxBrightness))
                    {
                        var actualBrightness = (int)(maxBrightness * (level / 100.0));
                        var result = await ExecuteShellCommandAsync($"echo {actualBrightness} | sudo tee {brightnessPath}");
                        return result.Success;
                    }
                }
            }

            // Fallback to xrandr if available
            var xrandrResult = await ExecuteShellCommandAsync($"xrandr --output $(xrandr | grep ' connected' | cut -d' ' -f1 | head -n1) --brightness {level / 100.0:F2}");
            return xrandrResult.Success;
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
            var brightnessPath = await GetBrightnessControlPathAsync();
            if (!string.IsNullOrEmpty(brightnessPath))
            {
                var currentBrightnessStr = await File.ReadAllTextAsync(brightnessPath);
                var maxBrightnessPath = Path.Combine(Path.GetDirectoryName(brightnessPath), "max_brightness");

                if (File.Exists(maxBrightnessPath))
                {
                    var maxBrightnessStr = await File.ReadAllTextAsync(maxBrightnessPath);

                    if (int.TryParse(currentBrightnessStr.Trim(), out int currentBrightness) &&
                        int.TryParse(maxBrightnessStr.Trim(), out int maxBrightness) &&
                        maxBrightness > 0)
                    {
                        return (int)((currentBrightness / (double)maxBrightness) * 100);
                    }
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
            var result = await ExecuteShellCommandAsync("nmcli radio wifi on");
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
            var result = await ExecuteShellCommandAsync("nmcli radio wifi off");
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
            var result = await ExecuteShellCommandAsync("nmcli radio wifi");
            return result.Success && result.Output.Trim().ToLower() == "enabled";
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
            var result = await ExecuteShellCommandAsync("bluetoothctl power on");
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
            var result = await ExecuteShellCommandAsync("bluetoothctl power off");
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
            var result = await ExecuteShellCommandAsync("bluetoothctl show | grep 'Powered'");
            return result.Success && result.Output.Contains("yes");
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
            var result = await ExecuteShellCommandAsync("systemctl suspend");
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
            // Try different lock methods based on desktop environment
            var lockCommands = new[]
            {
                "loginctl lock-session",
                "gnome-screensaver-command -l",
                "xdg-screensaver lock",
                "i3lock",
                "slock"
            };

            foreach (var command in lockCommands)
            {
                var result = await ExecuteShellCommandAsync($"which {command.Split(' ')[0]}");
                if (result.Success)
                {
                    var lockResult = await ExecuteShellCommandAsync(command);
                    if (lockResult.Success)
                        return true;
                }
            }
            return false;
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
            var muteResult = await ExecuteShellCommandAsync("amixer -D pulse get Master");
            status.IsMuted = muteResult.Success && muteResult.Output.Contains("[off]");

            // Brightness
            status.BrightnessLevel = await GetScreenBrightnessAsync();

            // WiFi
            status.IsWiFiEnabled = await GetWiFiStatusAsync();
            var wifiNetwork = await ExecuteShellCommandAsync("nmcli -t -f active,ssid dev wifi | grep '^yes'");
            status.WiFiStatus = wifiNetwork.Success ? wifiNetwork.Output.Split(':').LastOrDefault()?.Trim() ?? "Not connected" : "Unknown";

            // Bluetooth
            status.IsBluetoothEnabled = await GetBluetoothStatusAsync();
            status.BluetoothStatus = status.IsBluetoothEnabled ? "On" : "Off";

            // Battery
            if (Directory.Exists("/sys/class/power_supply"))
            {
                var batteryDirs = Directory.GetDirectories("/sys/class/power_supply", "BAT*");
                if (batteryDirs.Length > 0)
                {
                    var batteryDir = batteryDirs[0];
                    var capacityFile = Path.Combine(batteryDir, "capacity");
                    var statusFile = Path.Combine(batteryDir, "status");

                    if (File.Exists(capacityFile))
                    {
                        var capacityStr = await File.ReadAllTextAsync(capacityFile);
                        if (int.TryParse(capacityStr.Trim(), out int batteryLevel))
                        {
                            status.BatteryLevel = batteryLevel;
                        }
                    }

                    if (File.Exists(statusFile))
                    {
                        var statusStr = await File.ReadAllTextAsync(statusFile);
                        status.IsCharging = statusStr.Trim().ToLower().Contains("charging");
                        status.PowerStatus = statusStr.Trim();
                    }
                }
            }

            // CPU Usage
            var cpuResult = await ExecuteShellCommandAsync("top -bn1 | grep 'Cpu(s)'");
            if (cpuResult.Success)
            {
                var cpuMatch = Regex.Match(cpuResult.Output, @"(\d+\.\d+)%us");
                if (cpuMatch.Success && double.TryParse(cpuMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double cpuUsage))
                {
                    status.CpuUsage = cpuUsage;
                }
            }

            // Memory Usage
            if (File.Exists("/proc/meminfo"))
            {
                var memInfo = await File.ReadAllTextAsync("/proc/meminfo");
                var totalMatch = Regex.Match(memInfo, @"MemTotal:\s+(\d+) kB");
                var availableMatch = Regex.Match(memInfo, @"MemAvailable:\s+(\d+) kB");

                if (totalMatch.Success && availableMatch.Success &&
                    long.TryParse(totalMatch.Groups[1].Value, out long totalKb) &&
                    long.TryParse(availableMatch.Groups[1].Value, out long availableKb))
                {
                    status.TotalMemory = totalKb * 1024;
                    status.MemoryUsage = (totalKb - availableKb) * 1024;
                }
            }

            status.LastUpdated = DateTime.Now;
        }
        catch
        {
            // Return partial status even if some operations fail
        }

        return status;
    }

    private async Task<string> GetBrightnessControlPathAsync()
    {
        try
        {
            var backLightDirs = new[]
            {
                "/sys/class/backlight",
                "/sys/class/leds"
            };

            foreach (var dir in backLightDirs)
            {
                if (Directory.Exists(dir))
                {
                    var subDirs = Directory.GetDirectories(dir);
                    foreach (var subDir in subDirs)
                    {
                        var brightnessFile = Path.Combine(subDir, "brightness");
                        if (File.Exists(brightnessFile))
                        {
                            return brightnessFile;
                        }
                    }
                }
            }
            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
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