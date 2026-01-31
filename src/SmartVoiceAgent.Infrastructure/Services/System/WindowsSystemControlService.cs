using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models;
using System.Diagnostics;
using System.Management;

namespace SmartVoiceAgent.Infrastructure.Services.System;

public class WindowsSystemControlService : ISystemControlService
{
    private readonly SemaphoreSlim _volumeLock = new(1, 1);
    private int? _lastKnownVolume;

    public async Task<bool> SetSystemVolumeAsync(int level)
    {
        try
        {
            level = Math.Clamp(level, 0, 100);
            
            // Use NAudio for more reliable volume control on Windows
            try
            {
                using var deviceEnumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                using var device = deviceEnumerator.GetDefaultAudioEndpoint(
                    NAudio.CoreAudioApi.DataFlow.Render, 
                    NAudio.CoreAudioApi.Role.Multimedia);
                
                device.AudioEndpointVolume.MasterVolumeLevelScalar = level / 100f;
                _lastKnownVolume = level;
                return true;
            }
            catch
            {
                // Fallback to PowerShell
                var script = $@"
                    Add-Type -TypeDefinition @'
                    using System;
                    using System.Runtime.InteropServices;
                    public class Audio {{
                        [DllImport(""user32.dll"")]
                        public static extern IntPtr SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
                    }}
'@
                    $wsh = New-Object -ComObject WScript.Shell
                    $volume = [math]::Round({level} / 2)
                    for($i=0; $i -lt 50; $i++) {{ $wsh.SendKeys([char]174) }}
                    for($i=0; $i -lt $volume; $i++) {{ $wsh.SendKeys([char]175) }}
                ";
                return await ExecutePowerShellCommand(script);
            }
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
        try
        {
            using var deviceEnumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            using var device = deviceEnumerator.GetDefaultAudioEndpoint(
                NAudio.CoreAudioApi.DataFlow.Render, 
                NAudio.CoreAudioApi.Role.Multimedia);
            
            device.AudioEndpointVolume.Mute = true;
            return true;
        }
        catch
        {
            return await ExecutePowerShellCommand("$wsh = New-Object -ComObject WScript.Shell; $wsh.SendKeys([char]173)");
        }
    }

    public async Task<bool> UnmuteSystemVolumeAsync()
    {
        try
        {
            using var deviceEnumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            using var device = deviceEnumerator.GetDefaultAudioEndpoint(
                NAudio.CoreAudioApi.DataFlow.Render, 
                NAudio.CoreAudioApi.Role.Multimedia);
            
            device.AudioEndpointVolume.Mute = false;
            return true;
        }
        catch
        {
            return await ExecutePowerShellCommand("$wsh = New-Object -ComObject WScript.Shell; $wsh.SendKeys([char]173)");
        }
    }

    public async Task<int> GetSystemVolumeAsync()
    {
        try
        {
            using var deviceEnumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            using var device = deviceEnumerator.GetDefaultAudioEndpoint(
                NAudio.CoreAudioApi.DataFlow.Render, 
                NAudio.CoreAudioApi.Role.Multimedia);
            
            var volume = (int)(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
            _lastKnownVolume = volume;
            return volume;
        }
        catch
        {
            return _lastKnownVolume ?? 50;
        }
    }

    public async Task<bool> SetScreenBrightnessAsync(int level)
    {
        try
        {
            level = Math.Clamp(level, 0, 100);
            var script = $@"
                try {{
                    $brightness = Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightnessMethods -ErrorAction Stop
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
                    $brightness = Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightness -ErrorAction Stop
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

    #region WiFi Control

    public async Task<bool> EnableWiFiAsync()
    {
        try
        {
            // Try multiple methods to enable WiFi
            var script = @"
                try {
                    # Method 1: Try using netsh with common interface names
                    $interfaceNames = @('Wi-Fi', 'WiFi', 'Wireless Network Connection', 'WLAN')
                    $success = $false
                    
                    foreach ($name in $interfaceNames) {
                        try {
                            $result = netsh interface set interface name=""$name"" admin=enable 2>&1
                            if ($LASTEXITCODE -eq 0) {
                                $success = $true
                                break
                            }
                        } catch {}
                    }
                    
                    # Method 2: Try using Get-NetAdapter
                    if (-not $success) {
                        try {
                            $wifiAdapter = Get-NetAdapter | Where-Object { 
                                $_.Name -like '*Wi*Fi*' -or 
                                $_.Name -like '*Wireless*' -or 
                                $_.InterfaceDescription -like '*Wireless*'
                            } | Select-Object -First 1
                            
                            if ($wifiAdapter) {
                                Enable-NetAdapter -Name $wifiAdapter.Name -Confirm:$false
                                $success = $true
                            }
                        } catch {}
                    }
                    
                    if ($success) { Write-Output 'Success' } else { Write-Output 'Failed' }
                } catch {
                    Write-Output 'Error: ' + $_.Exception.Message
                }
            ";
            
            var result = await ExecutePowerShellCommandWithOutput(script);
            return result?.Contains("Success") == true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error enabling WiFi: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DisableWiFiAsync()
    {
        try
        {
            var script = @"
                try {
                    # Method 1: Try using netsh with common interface names
                    $interfaceNames = @('Wi-Fi', 'WiFi', 'Wireless Network Connection', 'WLAN')
                    $success = $false
                    
                    foreach ($name in $interfaceNames) {
                        try {
                            $result = netsh interface set interface name=""$name"" admin=disable 2>&1
                            if ($LASTEXITCODE -eq 0) {
                                $success = $true
                                break
                            }
                        } catch {}
                    }
                    
                    # Method 2: Try using Get-NetAdapter
                    if (-not $success) {
                        try {
                            $wifiAdapter = Get-NetAdapter | Where-Object { 
                                $_.Name -like '*Wi*Fi*' -or 
                                $_.Name -like '*Wireless*' -or 
                                $_.InterfaceDescription -like '*Wireless*'
                            } | Select-Object -First 1
                            
                            if ($wifiAdapter) {
                                Disable-NetAdapter -Name $wifiAdapter.Name -Confirm:$false
                                $success = $true
                            }
                        } catch {}
                    }
                    
                    if ($success) { Write-Output 'Success' } else { Write-Output 'Failed' }
                } catch {
                    Write-Output 'Error: ' + $_.Exception.Message
                }
            ";
            
            var result = await ExecutePowerShellCommandWithOutput(script);
            return result?.Contains("Success") == true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error disabling WiFi: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> GetWiFiStatusAsync()
    {
        try
        {
            var script = @"
                try {
                    $wifiAdapter = Get-NetAdapter | Where-Object { 
                        $_.Name -like '*Wi*Fi*' -or 
                        $_.Name -like '*Wireless*' -or 
                        $_.InterfaceDescription -like '*Wireless*'
                    } | Select-Object -First 1
                    
                    if ($wifiAdapter) {
                        $wifiAdapter.Status -eq 'Up'
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

    #endregion

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
try {
    $battery = Get-CimInstance -ClassName Win32_Battery -ErrorAction SilentlyContinue
    if ($battery) {
        $charge = $battery.EstimatedChargeRemaining
        $charging = $battery.BatteryStatus -eq 2
        Write-Output ($charge + '|' + $charging)
    } else {
        Write-Output '100|False'
    }
} catch {
    Write-Output '100|False'
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
try {
    $cpu = Get-CimInstance -ClassName Win32_Processor -ErrorAction SilentlyContinue | Measure-Object -Property LoadPercentage -Average
    $memory = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction SilentlyContinue
    if ($memory) {
        $totalMemory = [math]::Round($memory.TotalVisibleMemorySize / 1MB, 2)
        $freeMemory = [math]::Round($memory.FreePhysicalMemory / 1MB, 2)
        $usedMemory = $totalMemory - $freeMemory
        $cpuAvg = if ($cpu.Average) { $cpu.Average } else { 0 }
        Write-Output ($cpuAvg + '|' + $usedMemory + '|' + $totalMemory)
    } else {
        Write-Output '0|0|0'
    }
} catch {
    Write-Output '0|0|0'
}
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
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\"\"")}\"",
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
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\"\"")}\"",
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
