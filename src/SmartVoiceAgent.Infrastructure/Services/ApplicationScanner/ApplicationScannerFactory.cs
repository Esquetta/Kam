using SmartVoiceAgent.Core.Interfaces;
using System.Runtime.InteropServices;

namespace SmartVoiceAgent.Infrastructure.Services.ApplicationScanner;
/// <summary>
/// Factory for creating appropriate application scanner based on the current operating system.
/// </summary>
public static class ApplicationScannerFactory
{
    public static IApplicationScanner Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsApplicationScanner();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxApplicationScanner();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOSApplicationScanner();
        }
        else
        {
            throw new PlatformNotSupportedException($"Platform {RuntimeInformation.OSDescription} is not supported.");
        }
    }
}