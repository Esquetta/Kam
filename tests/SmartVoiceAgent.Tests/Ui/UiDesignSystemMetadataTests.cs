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
        selectors.Should().Contain("Button.SecondaryAction");

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
