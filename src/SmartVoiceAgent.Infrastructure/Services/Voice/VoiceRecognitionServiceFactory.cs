using Microsoft.Extensions.Configuration;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services.Voice;

/// <summary>
/// Creates the recorder for this platform, using the microphone chosen in Settings (<c>Voice:InputDeviceId</c>).
/// </summary>
public sealed class VoiceRecognitionServiceFactory : IVoiceRecognitionFactory
{
    private readonly IConfiguration? _configuration;

    /// <summary>
    /// Creates a factory that records from the default microphone.
    /// </summary>
    public VoiceRecognitionServiceFactory()
    {
    }

    /// <summary>
    /// Creates a factory that follows the microphone chosen in Settings.
    /// </summary>
    /// <param name="configuration">The configuration Settings write to.</param>
    public VoiceRecognitionServiceFactory(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <inheritdoc />
    public IVoiceRecognitionService Create()
    {
        if (OperatingSystem.IsWindows())
        {
            var deviceId = _configuration is null ? null : VoiceSettings.Read(_configuration).InputDeviceId;
            return new WindowsVoiceRecognitionService(deviceId);
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOSVoiceRecognitionService();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxVoiceRecognitionService();
        }

        throw new PlatformNotSupportedException();
    }
}
