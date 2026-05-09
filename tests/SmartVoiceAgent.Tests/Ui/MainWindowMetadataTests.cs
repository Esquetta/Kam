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

    [Fact]
    public void MainWindow_LogPanelUsesProductCopy()
    {
        var mainWindow = XDocument.Load(FindMainWindowXamlPath()).Root;

        var visibleText = mainWindow!
            .Descendants()
            .Where(element => element.Name.LocalName is "TextBlock" or "Button")
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();

        visibleText.Should().Contain("ACTIVITY_LOG");
        visibleText.Should().Contain("PLAN_TRACE");
        visibleText.Should().Contain("SKILL_RESULTS");
        visibleText.Should().NotContain(value =>
            value.Contains("KERNEL_LOG", StringComparison.Ordinal)
            || value.Contains("PLANNER_TRACE", StringComparison.Ordinal)
            || value.Contains("RESULT_VIEWER", StringComparison.Ordinal)
            || value.Contains("Coordinator AI", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MainWindow_ExposesRuntimeDiagnosticsNavigation()
    {
        var mainWindowText = File.ReadAllText(FindMainWindowXamlPath());

        mainWindowText.Should().Contain("NavigateToDiagnosticsCommand");
        mainWindowText.Should().Contain("RuntimeDiagnosticsViewModel");
        mainWindowText.Should().Contain("RuntimeDiagnosticsView");
        mainWindowText.Should().Contain("ToolTip.Tip=\"Runtime Diagnostics\"");
    }

    [Fact]
    public void MainWindow_ExposesSlashCommandPaletteBindings()
    {
        var mainWindowText = File.ReadAllText(FindMainWindowXamlPath());

        mainWindowText.Should().Contain("IsSlashCommandPaletteVisible");
        mainWindowText.Should().Contain("SlashCommandSuggestions");
        mainWindowText.Should().Contain("SelectSlashCommandCommand");
        mainWindowText.Should().Contain("type / for commands");
    }

    [Fact]
    public void MainWindow_BottomNavigationOnlyRendersThemeToggle()
    {
        var mainWindow = XDocument.Load(FindMainWindowXamlPath()).Root;

        var bottomNavigation = mainWindow!
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "StackPanel"
                && AttributeValue(element, "Grid.Row") == "2"
                && AttributeValue(element, "Margin") == "12,0");

        bottomNavigation
            .Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Should()
            .ContainSingle(button =>
                AttributeValue(button, "Command") == "{Binding ToggleThemeCommand}"
                && AttributeValue(button, "ToolTip.Tip") == "Toggle Theme");

        bottomNavigation
            .Descendants()
            .Where(element => element.Name.LocalName == "Border")
            .Should()
            .BeEmpty();
    }

    private static string? AttributeValue(XElement element, string attributeName)
    {
        return element
            .Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == attributeName)
            ?.Value;
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
