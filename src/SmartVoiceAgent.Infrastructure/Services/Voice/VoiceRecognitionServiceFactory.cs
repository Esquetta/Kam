using SmartVoiceAgent.Core.Interfaces;

public static class VoiceRecognitionServiceFactory
{
    public static IVoiceRecognitionService Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsVoiceRecognitionService();
        if (OperatingSystem.IsMacOS())
            return new MacOSVoiceRecognitionService();
        if (OperatingSystem.IsLinux())
            return new LinuxVoiceRecognitionService();

        throw new PlatformNotSupportedException();
    }
}
