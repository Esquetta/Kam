﻿using Microsoft.Win32;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Interfaces;
using System.Diagnostics;

namespace SmartVoiceAgent.Infrastructure.Services.ApplicationScanner;
/// <summary>
/// Windows-specific application scanner implementation.
/// </summary>
public class WindowsApplicationScanner : IApplicationScanner
{
    private readonly string[] _registryKeys = {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    };

    public async Task<IEnumerable<AppInfoDTO>> GetInstalledApplicationsAsync()
    {
        return await Task.Run(() =>
        {
            var apps = new List<AppInfoDTO>();
            var runningProcesses = GetRunningProcesses();

            foreach (var keyPath in _registryKeys)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                    if (key == null) continue;

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            if (subKey == null) continue;

                            var displayName = subKey.GetValue("DisplayName")?.ToString();
                            var installLocation = subKey.GetValue("InstallLocation")?.ToString();
                            var displayIcon = subKey.GetValue("DisplayIcon")?.ToString();
                            var uninstallString = subKey.GetValue("UninstallString")?.ToString();

                            if (string.IsNullOrWhiteSpace(displayName)) continue;

                            // Try to get executable path
                            var executablePath = GetExecutablePath(installLocation, displayIcon, uninstallString);
                            var isRunning = IsApplicationRunning(displayName, executablePath, runningProcesses);

                            apps.Add(new AppInfoDTO(displayName, executablePath ?? string.Empty, isRunning));
                        }
                        catch (Exception ex)
                        {
                            // Log exception if needed, continue with next app
                            Console.WriteLine($"Error reading registry subkey {subKeyName}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error accessing registry key {keyPath}: {ex.Message}");
                }
            }

            return apps.DistinctBy(a => a.Name).OrderBy(a => a.Name);
        });
    }

    private Dictionary<string, string> GetRunningProcesses()
    {
        var processes = new Dictionary<string, string>();
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (!string.IsNullOrEmpty(process.ProcessName) && !string.IsNullOrEmpty(process.MainModule?.FileName))
                    {
                        processes[process.ProcessName.ToLower()] = process.MainModule.FileName;
                    }
                }
                catch
                {
                    // Some processes may not be accessible
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting running processes: {ex.Message}");
        }
        return processes;
    }

    private string GetExecutablePath(string installLocation, string displayIcon, string uninstallString)
    {
        // Try install location first
        if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
        {
            var exeFiles = Directory.GetFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly);
            if (exeFiles.Length > 0)
                return exeFiles[0];
        }

        // Try display icon
        if (!string.IsNullOrEmpty(displayIcon) && File.Exists(displayIcon))
        {
            return displayIcon;
        }

        // Try uninstall string
        if (!string.IsNullOrEmpty(uninstallString))
        {
            var parts = uninstallString.Split('"');
            if (parts.Length > 1 && File.Exists(parts[1]))
            {
                return parts[1];
            }
        }

        return null;
    }

    private bool IsApplicationRunning(string appName, string executablePath, Dictionary<string, string> runningProcesses)
    {
        if (string.IsNullOrEmpty(executablePath))
            return false;

        var fileName = Path.GetFileNameWithoutExtension(executablePath).ToLower();
        return runningProcesses.ContainsKey(fileName);
    }
}