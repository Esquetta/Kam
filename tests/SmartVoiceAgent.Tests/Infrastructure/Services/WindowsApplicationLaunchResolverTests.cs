using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Services.Application;
using System.Runtime.Versioning;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

[SupportedOSPlatform("windows")]
public class WindowsApplicationLaunchResolverTests
{
    [Fact]
    public void CreateRegisteredProtocolUri_ExactProtocolMatch_ReturnsLaunchUri()
    {
        var uri = WindowsApplicationLaunchResolver.CreateRegisteredProtocolUri(
            "Spotify",
            ["spotify"]);

        uri.Should().Be("spotify:");
    }

    [Fact]
    public void CreateRegisteredProtocolUri_CompactProtocolMatch_ReturnsLaunchUri()
    {
        var uri = WindowsApplicationLaunchResolver.CreateRegisteredProtocolUri(
            "Visual Studio Code",
            ["visualstudiocode"]);

        uri.Should().Be("visualstudiocode:");
    }

    [Fact]
    public void CreateRegisteredProtocolUri_UnregisteredProtocol_ReturnsNull()
    {
        var uri = WindowsApplicationLaunchResolver.CreateRegisteredProtocolUri(
            "Spotify",
            ["mailto", "ms-settings"]);

        uri.Should().BeNull();
    }

    [Theory]
    [InlineData(@"C:\Users\agent\AppData\Local\Microsoft\WindowsApps\Spotify.exe")]
    [InlineData(@"C:\Users\agent\AppData\Local\Microsoft\WindowsApps\SpotifyAB.SpotifyMusic_zpdnekdrzrea0")]
    public void IsWindowsAppsExecutionAlias_WindowsAppsPath_ReturnsTrue(string path)
    {
        WindowsApplicationLaunchResolver.IsWindowsAppsExecutionAlias(path).Should().BeTrue();
    }
}
