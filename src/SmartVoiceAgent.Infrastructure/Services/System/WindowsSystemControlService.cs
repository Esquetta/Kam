using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models;
using System.Diagnostics;

namespace SmartVoiceAgent.Infrastructure.Services.System;
public class WindowsSystemControlService : ISystemControlService
{
    public async Task<bool> SetSystemVolumeAsync(int level)
    {
        try
        {
            level = Math.Clamp(level, 0, 100);
            var script = $@"
                Add-Type -TypeDefinition @'
                using System;
                using System.Runtime.InteropServices;
                public class AudioEndpointVolume {{
                    [DllImport(""ole32.dll"")]
                    public static extern int CoCreateInstance(ref Guid clsid, IntPtr pUnkOuter, int dwClsContext, ref Guid iid, out IntPtr ppv);
                    [DllImport(""ole32.dll"")]
                    public static extern int CoInitialize(IntPtr pvReserved);
                }}
'@
                [void][System.Reflection.Assembly]::LoadWithPartialName('Microsoft.VisualBasic')
                [Microsoft.VisualBasic.Interaction]::AppActivate((Get-Process -Name 'explorer').Id)
                for($i=0; $i -lt 50; $i++) {{ [System.Windows.Forms.SendKeys]::SendWait('{{VOLUMEDOWN}}') }}
                for($i=0; $i -lt {level / 2}; $i++) {{ [System.Windows.Forms.SendKeys]::SendWait('{{VOLUMEUP}}') }}
            ";

            return await ExecutePowerShellCommand(script);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting system volume: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> IncreaseSystemVolumeAsync(int step = 10)
    {
        var currentVolume = await GetSystemVolumeAsync();
        return await SetSystemVolumeAsync(Math.Min(100, currentVolume + step));
    }

    public async Task<bool> DecreaseSystemVolumeAsync(int step = 10)
    {
        var currentVolume = await GetSystemVolumeAsync();
        return await SetSystemVolumeAsync(Math.Max(0, currentVolume - step));
    }

    public async Task<bool> MuteSystemVolumeAsync()
    {
        return await ExecutePowerShellCommand("[void][System.Windows.Forms.SendKeys]::SendWait('{VOLUMEMUTE}')");
    }

    public async Task<bool> UnmuteSystemVolumeAsync()
    {
        return await ExecutePowerShellCommand("[void][System.Windows.Forms.SendKeys]::SendWait('{VOLUMEMUTE}')");
    }

    public async Task<int> GetSystemVolumeAsync()
    {
        try
        {
            var script = @"
                Add-Type -AssemblyName System.Windows.Forms
                $wshShell = New-Object -ComObject WScript.Shell
                $volume = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Internet Explorer\LowRegistry\Audio\PolicyConfig\PropertyStore').GetSubKeyNames()
                50  # Default fallback
            ";

            var result = await ExecutePowerShellCommandWithOutput(script);
            return int.TryParse(result?.Trim(), out var volume) ? volume : 50;
        }
        catch
        {
            return 50; // Default fallback
        }
    }

    public async Task<bool> SetScreenBrightnessAsync(int level)
    {
        try
        {
            level = Math.Clamp(level, 0, 100);
            var script = $@"
                try {{
                    $brightness = Get-WmiObject -Namespace root/WMI -Class WmiMonitorBrightnessMethods -ErrorAction Stop
                    if ($brightness) {{
                        $brightness.WmiSetBrightness(1, {level})
                        Write-Output 'Success'
                    }} else {{
                        Write-Output 'No brightness control available'
                    }}
                }} catch {{
                    Write-Output 'Error: ' + $_.Exception.Message
                }}
            ";

            return await ExecutePowerShellCommand(script);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting screen brightness: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> IncreaseScreenBrightnessAsync(int step = 10)
    {
        var currentBrightness = await GetScreenBrightnessAsync();
        return await SetScreenBrightnessAsync(Math.Min(100, currentBrightness + step));
    }

    public async Task<bool> DecreaseScreenBrightnessAsync(int step = 10)
    {
        var currentBrightness = await GetScreenBrightnessAsync();
        return await SetScreenBrightnessAsync(Math.Max(0, currentBrightness - step));
    }

    public async Task<int> GetScreenBrightnessAsync()
    {
        try
        {
            var script = @"
                try {
                    $brightness = Get-WmiObject -Namespace root/WMI -Class WmiMonitorBrightness -ErrorAction Stop
                    if ($brightness) {
                        $brightness.CurrentBrightness
                    } else {
                        50
                    }
                } catch {
                    50
                }
            ";

            var result = await ExecutePowerShellCommandWithOutput(script);
            return int.TryParse(result?.Trim(), out var brightness) ? brightness : 50;
        }
        catch
        {
            return 50;
        }
    }

    public async Task<bool> EnableWiFiAsync()
    {
        return await ExecutePowerShellCommand("netsh interface set interface name=\"Wi-Fi\" admin=enable");
    }

    public async Task<bool> DisableWiFiAsync()
    {
        return await ExecutePowerShellCommand("netsh interface set interface name=\"Wi-Fi\" admin=disable");
    }

    public async Task<bool> GetWiFiStatusAsync()
    {
        try
        {
            var result = await ExecutePowerShellCommandWithOutput("(Get-NetAdapter -Name 'Wi-Fi' -ErrorAction SilentlyContinue).Status");
            return result?.Trim().Equals("Up", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> EnableBluetoothAsync()
    {
        var script = @"
            try {
                Add-Type -AssemblyName System.Runtime.WindowsRuntime
                $asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | ? { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
                Function Await($WinRtTask, $ResultType) {
                    $asTask = $asTaskGeneric.MakeGenericMethod($ResultType)
                    $netTask = $asTask.Invoke($null, @($WinRtTask))
                    $netTask.Wait(-1) | Out-Null
                    $netTask.Result
                }
                [Windows.Devices.Radios.Radio,Windows.System.Devices,ContentType=WindowsRuntime] | Out-Null
                [Windows.Devices.Radios.RadioAccessStatus,Windows.System.Devices,ContentType=WindowsRuntime] | Out-Null
                Await ([Windows.Devices.Radios.Radio]::RequestAccessAsync()) ([Windows.Devices.Radios.RadioAccessStatus]) | Out-Null
                $radios = Await ([Windows.Devices.Radios.Radio]::GetRadiosAsync()) ([System.Collections.Generic.IReadOnlyList[Windows.Devices.Radios.Radio]])
                $bluetooth = $radios | ? { $_.Kind -eq 'Bluetooth' }
                if ($bluetooth) {
                    [Windows.Devices.Radios.RadioState,Windows.System.Devices,ContentType=WindowsRuntime] | Out-Null
                    Await ($bluetooth.SetStateAsync('On')) ([Windows.Devices.Radios.RadioAccessStatus]) | Out-Null
                    Write-Output 'Success'
                } else {
                    Write-Output 'No Bluetooth adapter found'
                }
            } catch {
                Write-Output 'Error: ' + $_.Exception.Message
            }
        ";

        return await ExecutePowerShellCommand(script);
    }

    public async Task<bool> DisableBluetoothAsync()
    {
        var script = @"
            try {
                Add-Type -AssemblyName System.Runtime.WindowsRuntime
                $asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | ? { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
                Function Await($WinRtTask, $ResultType) {
                    $asTask = $asTaskGeneric.MakeGenericMethod($ResultType)
                    $netTask = $asTask.Invoke($null, @($WinRtTask))
                    $netTask.Wait(-1) | Out-Null
                    $netTask.Result
                }
                [Windows.Devices.Radios.Radio,Windows.System.Devices,ContentType=WindowsRuntime] | Out-Null
                $radios = Await ([Windows.Devices.Radios.Radio]::GetRadiosAsync()) ([System.Collections.Generic.IReadOnlyList[Windows.Devices.Radios.Radio]])
                $bluetooth = $radios | ? { $_.Kind -eq 'Bluetooth' }
                if ($bluetooth) {
                    Await ($bluetooth.SetStateAsync('Off')) ([Windows.Devices.Radios.RadioAccessStatus]) | Out-Null
                    Write-Output 'Success'
                } else {
                    Write-Output 'No Bluetooth adapter found'
                }
            } catch {
                Write-Output 'Error: ' + $_.Exception.Message
            }
        ";

        return await ExecutePowerShellCommand(script);
    }

    public async Task<bool> GetBluetoothStatusAsync()
    {
        try
        {
            var script = @"
                try {
                    Add-Type -AssemblyName System.Runtime.WindowsRuntime
                    $asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | ? { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
                    Function Await($WinRtTask, $ResultType) {
                        $asTask = $asTaskGeneric.MakeGenericMethod($ResultType)
                        $netTask = $asTask.Invoke($null, @($WinRtTask))
                        $netTask.Wait(-1) | Out-Null
                        $netTask.Result
                    }
                    [Windows.Devices.Radios.Radio,Windows.System.Devices,ContentType=WindowsRuntime] | Out-Null
                    $radios = Await ([Windows.Devices.Radios.Radio]::GetRadiosAsync()) ([System.Collections.Generic.IReadOnlyList[Windows.Devices.Radios.Radio]])
                    $bluetooth = $radios | ? { $_.Kind -eq 'Bluetooth' }
                    if ($bluetooth) {
                        $bluetooth.State -eq 'On'
                    } else {
                        $false
                    }
                } catch {
                    $false
                }
            ";

            var result = await ExecutePowerShellCommandWithOutput(script);
            return bool.TryParse(result?.Trim(), out var status) && status;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ShutdownSystemAsync(int delayMinutes = 0)
    {
        var delaySeconds = delayMinutes * 60;
        return await ExecuteCommand("shutdown", $"/s /t {delaySeconds}");
    }

    public async Task<bool> RestartSystemAsync(int delayMinutes = 0)
    {
        var delaySeconds = delayMinutes * 60;
        return await ExecuteCommand("shutdown", $"/r /t {delaySeconds}");
    }

    public async Task<bool> SleepSystemAsync()
    {
        return await ExecuteCommand("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");
    }

    public async Task<bool> LockSystemAsync()
    {
        return await ExecuteCommand("rundll32.exe", "user32.dll,LockWorkStation");
    }

    public async Task<SystemStatusInfo> GetSystemStatusAsync()
    {
        var status = new SystemStatusInfo();

        try
        {
            // Get all system information in parallel
            var tasks = new[]
            {
                Task.Run(async () => status.VolumeLevel = await GetSystemVolumeAsync()),
                Task.Run(async () => status.BrightnessLevel = await GetScreenBrightnessAsync()),
                Task.Run(async () => status.IsWiFiEnabled = await GetWiFiStatusAsync()),
                Task.Run(async () => status.IsBluetoothEnabled = await GetBluetoothStatusAsync()),
                Task.Run(async () => await GetBatteryInfoAsync(status)),
                Task.Run(async () => await GetPerformanceInfoAsync(status))
            };

            await Task.WhenAll(tasks);

            status.WiFiStatus = status.IsWiFiEnabled ? "Connected" : "Disconnected";
            status.BluetoothStatus = status.IsBluetoothEnabled ? "On" : "Off";
            status.PowerStatus = status.IsCharging ? "Charging" : "On Battery";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting system status: {ex.Message}");
        }

        return status;
    }

    private async Task GetBatteryInfoAsync(SystemStatusInfo status)
    {
        try
        {
            var script = @"
                $battery = Get-WmiObject -Class Win32_Battery
                if ($battery) {
                    Write-Output ""$($battery.EstimatedChargeRemaining)|$($battery.BatteryStatus -eq 2)""
                } else {
                    Write-Output ""100|False""
                }
            ";

            var result = await ExecutePowerShellCommandWithOutput(script);
            if (!string.IsNullOrEmpty(result))
            {
                var parts = result.Split('|');
                if (parts.Length == 2)
                {
                    status.BatteryLevel = int.TryParse(parts[0], out var level) ? level : 100;
                    status.IsCharging = bool.TryParse(parts[1], out var charging) && charging;
                }
            }
        }
        catch
        {
            status.BatteryLevel = 100;
            status.IsCharging = false;
        }
    }

    private async Task GetPerformanceInfoAsync(SystemStatusInfo status)
    {
        try
        {
            var script = @"
                $cpu = Get-WmiObject -Class Win32_Processor | Measure-Object -Property LoadPercentage -Average
                $memory = Get-WmiObject -Class Win32_OperatingSystem
                $totalMemory = [math]::Round($memory.TotalVisibleMemorySize / 1MB, 2)
                $freeMemory = [math]::Round($memory.FreePhysicalMemory / 1MB, 2)
                $usedMemory = $totalMemory - $freeMemory
                Write-Output ""$($cpu.Average)|$usedMemory|$totalMemory""
            ";

            var result = await ExecutePowerShellCommandWithOutput(script);
            if (!string.IsNullOrEmpty(result))
            {
                var parts = result.Split('|');
                if (parts.Length == 3)
                {
                    status.CpuUsage = double.TryParse(parts[0], out var cpu) ? cpu : 0;
                    status.MemoryUsage = long.TryParse(parts[1], out var used) ? (long)(used * 1024 * 1024) : 0;
                    status.TotalMemory = long.TryParse(parts[2], out var total) ? (long)(total * 1024 * 1024) : 0;
                }
            }
        }
        catch
        {
            status.CpuUsage = 0;
            status.MemoryUsage = 0;
            status.TotalMemory = 0;
        }
    }

    private async Task<bool> ExecutePowerShellCommand(string command)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-Command \"{command}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(processInfo);
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> ExecutePowerShellCommandWithOutput(string command)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-Command \"{command}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(processInfo);
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<bool> ExecuteCommand(string fileName, string arguments)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
