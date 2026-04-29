using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;

namespace SmartVoiceAgent.Ui.Services;

public interface IAutoStartRegistrationService
{
    bool IsSupported { get; }

    bool IsEnabled(bool fallback);

    void SetEnabled(bool enable, string? executablePath);
}

public sealed class WindowsAutoStartRegistrationService : IAutoStartRegistrationService
{
    private const string RunRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string CurrentValueName = "Kam";
    private static readonly string[] RegistryValueNames =
    [
        CurrentValueName,
        "KAM Neural Core",
        "SmartVoiceAgent"
    ];

    private readonly Func<bool> _isWindows;

    public WindowsAutoStartRegistrationService()
        : this(OperatingSystem.IsWindows)
    {
    }

    public WindowsAutoStartRegistrationService(Func<bool> isWindows)
    {
        _isWindows = isWindows;
    }

    public bool IsSupported => IsWindows;

    [SupportedOSPlatformGuard("windows")]
    private bool IsWindows => _isWindows();

    public bool IsEnabled(bool fallback)
    {
        if (!IsWindows)
        {
            return fallback;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: false);
            if (key is null)
            {
                return fallback;
            }

            foreach (var valueName in RegistryValueNames)
            {
                if (key.GetValue(valueName) is not null)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Auto-start: Failed to read registry state: {ex}");
            return fallback;
        }
    }

    public void SetEnabled(bool enable, string? executablePath)
    {
        if (!IsWindows)
        {
            Debug.WriteLine("Auto-start: Registry startup registration is not supported on this platform.");
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: true);
            if (key is null)
            {
                Debug.WriteLine("Auto-start: Could not open registry key");
                return;
            }

            if (enable)
            {
                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                {
                    Debug.WriteLine("Auto-start: Could not find valid executable path");
                    return;
                }

                key.SetValue(CurrentValueName, QuotePathIfNeeded(executablePath));
                Debug.WriteLine($"Auto-start enabled: {executablePath}");
                return;
            }

            foreach (var valueName in RegistryValueNames)
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }

            Debug.WriteLine("Auto-start disabled");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set auto-start: {ex}");
        }
    }

    private static string QuotePathIfNeeded(string executablePath)
    {
        return executablePath.Contains(' ') && !executablePath.StartsWith('"')
            ? $"\"{executablePath}\""
            : executablePath;
    }
}
