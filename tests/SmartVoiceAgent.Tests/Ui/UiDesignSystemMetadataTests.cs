using FluentAssertions;
using System.Xml.Linq;

namespace SmartVoiceAgent.Tests.Ui;

public sealed class UiDesignSystemMetadataTests
{
    [Fact]
    public void Controls_ExposeSharedWorkbenchPageChrome()
    {
        var controls = XDocument.Load(FindProjectFilePath("src", "Ui", "SmartVoiceAgent.Ui", "Themes", "Controls.axaml")).Root;
        var selectors = controls!
            .Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .Select(element => AttributeValue(element, "Selector"))
            .ToArray();

        selectors.Should().Contain("Grid.WorkbenchPage");
        selectors.Should().Contain("StackPanel.WorkbenchPage");
        selectors.Should().Contain("Border.WorkbenchSection");
        selectors.Should().Contain("TextBlock.PageTitle");
        selectors.Should().Contain("TextBlock.PageSubtitle");
        selectors.Should().Contain("Button.PrimaryAction");
        selectors.Should().Contain("Button.SecondaryAction");
        selectors.Should().Contain("Button.DestructiveAction");
        selectors.Should().Contain("Button.IconAction");
        selectors.Should().Contain("Button.CompactIconButton");
        selectors.Should().Contain("Button.CompactAction");
        selectors.Should().Contain("Border.IconBadge");

        var cardStyle = controls
            .Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && AttributeValue(element, "Selector") == "Border.Card");

        cardStyle.Elements()
            .Single(element => element.Name.LocalName == "Setter"
                && AttributeValue(element, "Property") == "CornerRadius")
            .Attribute("Value")!
            .Value
            .Should()
            .Be("8");

        foreach (var selector in new[] { "Button.PrimaryAction", "Button.SecondaryAction", "Button.DestructiveAction" })
        {
            var actionSetters = controls
                .Descendants()
                .Single(element => element.Name.LocalName == "Style"
                    && AttributeValue(element, "Selector") == selector)
                .Elements()
                .Where(element => element.Name.LocalName == "Setter")
                .ToDictionary(element => AttributeValue(element, "Property")!, element => AttributeValue(element, "Value"));

            actionSetters["HorizontalContentAlignment"].Should().Be("Center");
            actionSetters["VerticalContentAlignment"].Should().Be("Center");
        }
    }

    [Theory]
    [InlineData("MainWindow.axaml")]
    [InlineData("PluginsView.axaml")]
    public void CompactIconButtons_DoNotOverrideSharedDimensions(string viewFileName)
    {
        var view = XDocument.Load(FindProjectFilePath("src", "Ui", "SmartVoiceAgent.Ui", "Views", viewFileName)).Root;

        var compactButtons = view!
            .Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Where(element => (AttributeValue(element, "Classes") ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("CompactIconButton"))
            .ToArray();

        compactButtons.Should().NotBeEmpty();
        compactButtons.Should().OnlyContain(element =>
            AttributeValue(element, "Width") == null
            && AttributeValue(element, "Height") == null
            && AttributeValue(element, "MinWidth") == null
            && AttributeValue(element, "MinHeight") == null);
    }

    [Theory]
    [InlineData("SettingsView.axaml")]
    [InlineData("IntegrationsView.axaml")]
    [InlineData("PluginsView.axaml")]
    [InlineData("RuntimeDiagnosticsView.axaml")]
    public void PrimaryPages_OptIntoSharedWorkbenchChrome(string viewFileName)
    {
        var viewText = File.ReadAllText(FindProjectFilePath("src", "Ui", "SmartVoiceAgent.Ui", "Views", viewFileName));

        viewText.Should().Contain("Classes=\"WorkbenchPage\"");
        viewText.Should().Contain("Classes=\"PageTitle\"");
        viewText.Should().Contain("Classes=\"PageSubtitle\"");
    }

    [Theory]
    [InlineData("IntegrationsView.axaml")]
    [InlineData("PluginsView.axaml")]
    [InlineData("RuntimeDiagnosticsView.axaml")]
    [InlineData("SettingsView.axaml")]
    public void PrimaryPages_UseSharedActionAndIconLanguage(string viewFileName)
    {
        var viewText = File.ReadAllText(FindProjectFilePath("src", "Ui", "SmartVoiceAgent.Ui", "Views", viewFileName));

        var usesSharedActionOrIcon = viewText.Contains("Classes=\"PrimaryAction\"", StringComparison.Ordinal)
            || viewText.Contains("Classes=\"SecondaryAction\"", StringComparison.Ordinal)
            || viewText.Contains("Classes=\"IconAction\"", StringComparison.Ordinal)
            || viewText.Contains("Classes=\"IconBadge\"", StringComparison.Ordinal);

        usesSharedActionOrIcon.Should().BeTrue();
        viewText.Should().NotContain("LetterSpacing=\"16\"");
        viewText.Should().NotContain("FontSize=\"48\"");
        viewText.Should().NotContain("CornerRadius=\"12\"");
    }

    private static string? AttributeValue(XElement element, string attributeName)
    {
        return element
            .Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == attributeName)
            ?.Value;
    }

    private static string FindProjectFilePath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(segments)} from the test output directory.");
    }
}
