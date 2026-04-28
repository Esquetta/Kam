using FluentAssertions;

namespace SmartVoiceAgent.Tests.Ui;

public sealed class ProductCopySmokeTests
{
    [Fact]
    public void UserVisibleSourceCopy_DoesNotUseLegacyBranding()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "src", "Ui", "SmartVoiceAgent.Ui", "ViewModels", "MainWindowViewModel.cs"),
            Path.Combine(root, "src", "Ui", "SmartVoiceAgent.Ui", "Services", "Concrete", "TrayIconService.cs"),
            Path.Combine(root, "src", "Ui", "SmartVoiceAgent.Ui", "SmartVoiceAgent.Ui.csproj"),
            Path.Combine(root, "src", "SmartVoiceAgent.AgentHost.ConsoleApp", "Program.cs"),
            Path.Combine(root, "src", "SmartVoiceAgent.Infrastructure", "Services", "Message", "EmailMessageService.cs")
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);

            text.Should().NotContain("KAM NEURAL", file);
            text.Should().NotContain("KAM Neural Core", file);
            text.Should().NotContain("AI Voice Assistant", file);
            text.Should().NotContain("KERNEL_INITIALIZED", file);
            text.Should().NotContain("NEURAL_LINK_STABLE", file);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Kam.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from the test output directory.");
    }
}
