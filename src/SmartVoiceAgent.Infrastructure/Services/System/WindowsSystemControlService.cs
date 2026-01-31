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
        return await ExecutePowerShellCommand("$wsh = New-Object -ComObject WScript.Shell; $wsh.SendKeys([char]173)");
    }

    public async Task<bool> UnmuteSystemVolumeAsync()
    {
        return await ExecutePowerShellCommand("$wsh = New-Object -ComObject WScript.Shell; $wsh.SendKeys([char]173)");
    }

    public async Task<int> GetSystemVolumeAsync()
    {
        return 50; // Default fallback - NAudio would be better
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

    public async Task<bool> EnableWiFiAsync()
    {
        try
        {
            Console.WriteLine("🔍 Searching for WiFi adapters...");
            
            // First, let's see what adapters exist
            var checkScript = @"
Write-Output '=== All Network Adapters ==='
Get-NetAdapter | Select-Object Name, InterfaceDescription, Status | ForEach-Object { Write-Output ($_.Name + ' | ' + $_.InterfaceDescription + ' | ' + $_.Status) }
Write-Output '=== WiFi-like Adapters ==='
Get-NetAdapter | Where-Object { $_.Name -like '*Wi*' -or $_.Name -like '*Wireless*' -or $_.InterfaceDescription -like '*Wireless*' -or $_.InterfaceDescription -like '*Wi*Fi*' } | ForEach-Object { Write-Output ($_.Name + ' | ' + $_.Status) }
";
            var adapterInfo = await ExecutePowerShellCommandWithOutput(checkScript);
            Console.WriteLine($"📡 Adapters found:\n{adapterInfo}");

            // Try to enable using Get-NetAdapter
            var enableScript = @"
$success = $false
$wifiAdapter = Get-NetAdapter | Where-Object { 
    $_.Name -like '*Wi*' -or 
    $_.Name -like '*Wireless*' -or 
    $_.InterfaceDescription -like '*Wireless*' -or
    $_.InterfaceDescription -like '*Wi*Fi*'
} | Select-Object -First 1

if ($wifiAdapter) {
    Write-Output ('Found adapter: ' + $wifiAdapter.Name + ' - Current status: ' + $wifiAdapter.Status)
    try {
        Enable-NetAdapter -Name $wifiAdapter.Name -Confirm:$false -ErrorAction Stop
        Write-Output ('SUCCESS: Enabled ' + $wifiAdapter.Name)
        $success = $true
    } catch {
        Write-Output ('ERROR enabling: ' + $_.Exception.Message)
    }
} else {
    Write-Output 'ERROR: No WiFi adapter found'
}

if (-not $success) {
    # Fallback to netsh
    $interfaces = @('Wi-Fi', 'WiFi', 'Wireless Network Connection', 'WLAN', 'Wireless')
    foreach ($intf in $interfaces) {
        $output = netsh interface set interface name=""$intf"" admin=enable 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Output ('SUCCESS: Enabled via netsh: ' + $intf)
            $success = $true
            break
        } else {
            Write-Output ('netsh failed for ' + $intf + ': ' + $output)
        }
    }
}

Write-Output ('FINAL RESULT: Success=' + $success)
";
            
            var result = await ExecutePowerShellCommandWithOutput(enableScript);
            Console.WriteLine($"📄 Enable WiFi Result:\n{result}");
            
            var success = result?.Contains("SUCCESS:") == true;
            if (!success)
            {
                Console.WriteLine("⚠️ WiFi enable failed. Common causes:");
                Console.WriteLine("   1. No WiFi adapter detected");
                Console.WriteLine("   2. Running without Administrator privileges");
                Console.WriteLine("   3. WiFi adapter driver issues");
            }
            return success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Exception enabling WiFi: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DisableWiFiAsync()
    {
        try
        {
            Console.WriteLine("🔍 Searching for WiFi adapters to disable...");
            
            var script = @"
$success = $false
$wifiAdapter = Get-NetAdapter | Where-Object { 
    $_.Name -like '*Wi*' -or 
    $_.Name -like '*Wireless*' -or 
    $_.InterfaceDescription -like '*Wireless*' -or
    $_.InterfaceDescription -like '*Wi*Fi*'
} | Select-Object -First 1

if ($wifiAdapter) {
    Write-Output ('Found adapter: ' + $wifiAdapter.Name)
    try {
        Disable-NetAdapter -Name $wifiAdapter.Name -Confirm:$false -ErrorAction Stop
        Write-Output ('SUCCESS: Disabled ' + $wifiAdapter.Name)
        $success = $true
    } catch {
        Write-Output ('ERROR disabling: ' + $_.Exception.Message)
    }
} else {
    Write-Output 'ERROR: No WiFi adapter found'
}

if (-not $success) {
    # Fallback to netsh
    $interfaces = @('Wi-Fi', 'WiFi', 'Wireless Network Connection', 'WLAN', 'Wireless')
    foreach ($intf in $interfaces) {
        $output = netsh interface set interface name=""$intf"" admin=disable 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Output ('SUCCESS: Disabled via netsh: ' + $intf)
            $success = $true
            break
        } else {
            Write-Output ('netsh failed for ' + $intf + ': ' + $output)
        }
    }
}

Write-Output ('FINAL RESULT: Success=' + $success)
";
            
            var result = await ExecutePowerShellCommandWithOutput(script);
            Console.WriteLine($"📄 Disable WiFi Result:\n{result}");
            
            var success = result?.Contains("SUCCESS:") == true;
            return success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Exception disabling WiFi: {ex.Message}");
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
        $_.Name -like '*Wi*' -or 
        $_.Name -like '*Wireless*' -or 
        $_.InterfaceDescription -like '*Wireless*' -or
        $_.InterfaceDescription -like '*Wi*Fi*'
    } | Select-Object -First 1
    
    if ($wifiAdapter) {
        $isUp = $wifiAdapter.Status -eq 'Up'
        Write-Output ($isUp.ToString().ToLower())
    } else {
        Write-Output 'false'
    }
} catch {
    Write-Output 'false'
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

    public async Task<bool> EnableBluetoothAsync()
    {
        // Bluetooth implementation requires Windows Runtime APIs
        // This is a simplified version
        return await ExecutePowerShellCommand(
            "[Windows.Devices.Radios.Radio,Windows.System.Devices,ContentType=WindowsRuntime] | Out-Null; " +
            "$radios = [Windows.Devices.Radios.Radio]::GetRadiosAsync().AsTask().Result; " +
            "$bluetooth = $radios | Where-Object { $_.Kind -eq 'Bluetooth' }; " +
            "if ($bluetooth) { $bluetooth.SetStateAsync('On').AsTask().Wait(); Write-Output 'Success' }");
    }

    public async Task<bool> DisableBluetoothAsync()
    {
        return await ExecutePowerShellCommand(
            "[Windows.Devices.Radios.Radio,Windows.System.Devices,ContentType=WindowsRuntime] | Out-Null; " +
            "$radios = [Windows.Devices.Radios.Radio]::GetRadiosAsync().AsTask().Result; " +
            "$bluetooth = $radios | Where-Object { $_.Kind -eq 'Bluetooth' }; " +
            "if ($bluetooth) { $bluetooth.SetStateAsync('Off').AsTask().Wait(); Write-Output 'Success' }");
    }

    public async Task<bool> GetBluetoothStatusAsync()
    {
        try
        {
            var script = @"
try {
    [Windows.Devices.Radios.Radio,Windows.System.Devices,ContentType=WindowsRuntime] | Out-Null
    $radios = [Windows.Devices.Radios.Radio]::GetRadiosAsync().AsTask().Result
    $bluetooth = $radios | Where-Object { $_.Kind -eq 'Bluetooth' }
    if ($bluetooth) {
        ($bluetooth.State -eq 'On').ToString().ToLower()
    } else {
        'false'
    }
} catch {
    'false'
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
        var status = new SystemStatusInfo
        {
            VolumeLevel = await GetSystemVolumeAsync(),
            BrightnessLevel = await GetScreenBrightnessAsync(),
            IsWiFiEnabled = await GetWiFiStatusAsync(),
            IsBluetoothEnabled = await GetBluetoothStatusAsync()
        };
        
        status.WiFiStatus = status.IsWiFiEnabled ? "Connected" : "Disconnected";
        status.BluetoothStatus = status.IsBluetoothEnabled ? "On" : "Off";
        
        return status;
    }

    private async Task<bool> ExecutePowerShellCommand(string command)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
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
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(processInfo);
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine($"⚠️ PowerShell Error: {error}");
            }
            
            return output ?? string.Empty;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ PowerShell Execution Error: {ex.Message}");
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
