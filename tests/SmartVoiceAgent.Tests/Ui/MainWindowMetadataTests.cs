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
    public void MainWindow_LogPanelUsesCalmerActivityCopy()
    {
        var mainWindow = XDocument.Load(FindMainWindowXamlPath()).Root;

        var visibleText = mainWindow!
            .Descendants()
            .Where(element => element.Name.LocalName is "TextBlock" or "Button")
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();

        visibleText.Should().Contain("Activity");
        visibleText.Should().Contain("Live session events");
        visibleText.Should().Contain("Agents");
        visibleText.Should().Contain("Plan trace");
        visibleText.Should().Contain("Skill results");
        visibleText.Should().NotContain(value =>
            value.Contains("ACTIVITY_LOG", StringComparison.Ordinal)
            || value.Contains("KERNEL_LOG", StringComparison.Ordinal)
            || value.Contains("PENDING_CONFIRMATION", StringComparison.Ordinal)
            || value.Contains("PLANNER_TRACE", StringComparison.Ordinal)
            || value.Contains("RESULT_VIEWER", StringComparison.Ordinal)
            || value.Contains("Coordinator AI", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MainWindow_ActivityPanelUsesStructuredFeedBindings()
    {
        var mainWindowText = File.ReadAllText(FindMainWindowXamlPath());

        mainWindowText.Should().Contain("ItemsSource=\"{Binding ActivityLogEntries}\"");
        mainWindowText.Should().Contain("ItemsSource=\"{Binding RuntimeAgentActivities}\"");
        mainWindowText.Should().Contain("IsVisible=\"{Binding HasRuntimeAgentActivities}\"");
        mainWindowText.Should().Contain("Classes=\"ActivityLogItem\"");
        mainWindowText.Should().Contain("Text=\"{Binding CategoryText}\"");
        mainWindowText.Should().Contain("Text=\"{Binding SourceText}\"");
        mainWindowText.Should().Contain("Text=\"{Binding MessageText}\"");
        mainWindowText.Should().Contain("Text=\"{Binding TimeText}\"");
        mainWindowText.Should().Contain("Text=\"{Binding DisplayName}\"");
        mainWindowText.Should().Contain("Text=\"{Binding StatusText}\"");
        mainWindowText.Should().Contain("Text=\"{Binding LastMessage}\"");
        mainWindowText.Should().Contain("ShowRuntimeAgentRunDetailCommand");
        mainWindowText.Should().Contain("CommandParameter=\"{Binding}\"");
        mainWindowText.Should().Contain("IsVisible=\"{Binding HasSelectedRuntimeAgentRun}\"");
        mainWindowText.Should().Contain("SelectedRuntimeAgentRun.ModelIdText");
        mainWindowText.Should().Contain("SelectedRuntimeAgentRun.Observations");
        mainWindowText.Should().Contain("Text=\"{Binding SummaryText}\"");
        mainWindowText.Should().Contain("Run detail");
        mainWindowText.Should().Contain("Context");
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
        mainWindowText.Should().Contain("Type / for commands");
    }

    [Fact]
    public void MainWindow_SlashCommandPaletteUsesCalmSuggestionChrome()
    {
        var mainWindow = XDocument.Load(FindMainWindowXamlPath()).Root;
        var mainWindowText = File.ReadAllText(FindMainWindowXamlPath());

        var template = mainWindow!
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "DataTemplate"
                && AttributeValue(element, "DataType") == "vm:SlashCommandSuggestionViewModel");

        var suggestionButton = template
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && AttributeValue(element, "Classes") == "SlashCommandItem");

        var suggestionChrome = suggestionButton
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border"
                && AttributeValue(element, "Background")?.Contains("IsSelected", StringComparison.Ordinal) == true);

        AttributeValue(suggestionButton, "Command")
            .Should()
            .Be("{Binding $parent[Window].DataContext.SelectSlashCommandCommand}");
        AttributeValue(suggestionChrome, "Background")
            .Should()
            .Contain("ConverterParameter='CardBgHoverBrush|TransparentBrush'");
        AttributeValue(suggestionChrome, "BorderBrush")
            .Should()
            .Be("Transparent");
        AttributeValue(suggestionChrome, "HorizontalAlignment").Should().Be("Stretch");

        mainWindowText.Should().Contain("<Style Selector=\"Button.SlashCommandItem\">");
        mainWindowText.Should().Contain("<Style Selector=\"Button.SlashCommandItem /template/ ContentPresenter#PART_ContentPresenter\">");
        mainWindowText.Should().Contain("<Style Selector=\"Button.SlashCommandItem:pointerover /template/ ContentPresenter#PART_ContentPresenter\">");
        mainWindowText.Should().Contain("Opacity=\"{Binding IsSelected, Converter={StaticResource BoolToOpacityConverter}, ConverterParameter='1|0'}\"");
        mainWindowText.Should().Contain("HorizontalAlignment=\"Stretch\"");
    }

    [Fact]
    public void MainWindow_SlashCommandPaletteAvoidsAlertColorsAndShadowEffects()
    {
        var mainWindow = XDocument.Load(FindMainWindowXamlPath()).Root;

        var template = mainWindow!
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "DataTemplate"
                && AttributeValue(element, "DataType") == "vm:SlashCommandSuggestionViewModel");

        var templateText = string.Join(
            " ",
            template
                .DescendantsAndSelf()
                .SelectMany(element => element.Attributes())
                .Select(attribute => attribute.Value));

        templateText.Should().NotContain("AccentGreen");
        templateText.Should().NotContain("AccentError");
        templateText.Should().NotContain("Red");
        templateText.Should().NotContain("Green");
        templateText.Should().NotContain("BoxShadow");
        template
            .Descendants()
            .Should()
            .NotContain(element =>
                element.Name.LocalName.Contains("Shadow", StringComparison.OrdinalIgnoreCase)
                || element.Name.LocalName.Contains("BlurEffect", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MainWindow_SlashCommandPaletteBrushKeysExist()
    {
        var brushes = XDocument.Load(FindProjectFilePath("src", "Ui", "SmartVoiceAgent.Ui", "Themes", "Brushes.axaml")).Root;
        var brushKeys = brushes!
            .Descendants()
            .Select(element => AttributeValue(element, "Key"))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToArray();

        brushKeys.Should().Contain("AccentCyanBrush");
        brushKeys.Should().Contain("TransparentBrush");
        brushKeys.Should().Contain("CardBgHoverBrush");
        brushKeys.Should().Contain("CardBgBrush");
        brushKeys.Should().Contain("BorderSubtleBrush");
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
        return FindProjectFilePath("src", "Ui", "SmartVoiceAgent.Ui", "Views", "MainWindow.axaml");
    }

    private static string FindProjectFilePath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidateSegments = new[] { directory.FullName }
                .Concat(segments)
                .ToArray();
            var candidate = Path.Combine(candidateSegments);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(segments)} from the test output directory.");
    }
}
