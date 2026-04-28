using FluentAssertions;
using System.Xml.Linq;

namespace SmartVoiceAgent.Tests.Ui;

public sealed class MainWindowMetadataTests
{
    [Fact]
    public void MainWindow_TitleDoesNotRenderProductNameOverSystemStatus()
    {
        var mainWindow = XDocument.Load(FindMainWindowXamlPath()).Root;

        mainWindow.Should().NotBeNull();
        mainWindow!.Attribute("Title")?.Value.Should().BeEmpty();
        mainWindow.Attribute("Title")?.Value.Should().NotContain("COORDINATOR");
    }

    private static string FindMainWindowXamlPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Ui",
                "SmartVoiceAgent.Ui",
                "Views",
                "MainWindow.axaml");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate MainWindow.axaml from the test output directory.");
    }
}
