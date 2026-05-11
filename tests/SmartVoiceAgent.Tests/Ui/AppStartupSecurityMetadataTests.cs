using FluentAssertions;

namespace SmartVoiceAgent.Tests.Ui;

public sealed class AppStartupSecurityMetadataTests
{
    [Fact]
    public void AppStartup_DoesNotForceDevelopmentEnvironment()
    {
        var source = File.ReadAllText(FindAppSourcePath());

        source.Should().NotContain("Environment.SetEnvironmentVariable(\"DOTNET_ENVIRONMENT\", \"Development\")");
        source.Should().NotContain(".UseEnvironment(\"Development\")");
    }

    [Fact]
    public void AppStartup_OnlyLogsConfigurationDetailsWhenDiagnosticsAreEnabled()
    {
        var source = File.ReadAllText(FindAppSourcePath());

        source.Should().Contain("ShouldLogConfigurationDebugInfo(configuration)");
        source.Should().Contain("KAM_LOG_CONFIGURATION");
        source.Should().NotContain("dotnet user-secrets set \"AIService:ApiKey\"");
        source.Should().NotContain("dotnet user-secrets set \"HuggingFaceConfig:ApiKey\"");
    }

    [Fact]
    public void AppStartup_LogsAvaloniaDispatcherAndHostStartupFailures()
    {
        var source = File.ReadAllText(FindAppSourcePath());

        source.Should().Contain("Dispatcher.UIThread.UnhandledException");
        source.Should().Contain("StartHostAsync(_host)");
        source.Should().Contain("Host failed to start");
    }

    private static string FindAppSourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Ui",
                "SmartVoiceAgent.Ui",
                "App.axaml.cs");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate App.axaml.cs from the test output directory.");
    }
}
