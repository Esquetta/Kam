using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Services.Voice;

public  class VoiceRecognitionServiceFactory: IVoiceRecognitionFactory
{
    public  IVoiceRecognitionService Create()
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
