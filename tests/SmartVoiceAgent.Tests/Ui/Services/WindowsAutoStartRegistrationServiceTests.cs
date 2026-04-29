using FluentAssertions;
using SmartVoiceAgent.Ui.Services;

namespace SmartVoiceAgent.Tests.Ui.Services;

public class WindowsAutoStartRegistrationServiceTests
{
    [Fact]
    public void IsEnabled_WhenPlatformUnsupported_ReturnsPersistedFallback()
    {
        var service = new WindowsAutoStartRegistrationService(() => false);

        service.IsSupported.Should().BeFalse();
        service.IsEnabled(fallback: true).Should().BeTrue();
        service.IsEnabled(fallback: false).Should().BeFalse();
    }

    [Fact]
    public void SetEnabled_WhenPlatformUnsupported_DoesNotRequireExecutablePath()
    {
        var service = new WindowsAutoStartRegistrationService(() => false);

        var act = () => service.SetEnabled(enable: true, executablePath: null);

        act.Should().NotThrow();
    }
}
