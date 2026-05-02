using FluentAssertions;
using System.Xml.Linq;

namespace SmartVoiceAgent.Tests.Ui;

public sealed class PluginsViewIconLayoutTests
{
    [Fact]
    public void PluginCards_CenterStatusGlyphsInsideIconBadge()
    {
        var pluginsView = XDocument.Load(FindPluginsViewXamlPath()).Root;

        var iconBadge = pluginsView!
            .Descendants()
            .Single(element => element.Name.LocalName == "Grid"
                && AttributeValue(element, "Classes") == "PluginIconBadge");

        var stateGlyphs = iconBadge
            .Elements()
            .Where(element => element.Name.LocalName == "Ellipse"
                && AttributeValue(element, "Width") == "16"
                && AttributeValue(element, "Height") == "16")
            .ToArray();

        stateGlyphs.Should().HaveCount(2);
        stateGlyphs.Should().OnlyContain(element =>
            AttributeValue(element, "HorizontalAlignment") == "Center"
            && AttributeValue(element, "VerticalAlignment") == "Center");
    }

    [Fact]
    public void PluginCards_UseSharedIconButtonLayoutForActions()
    {
        var pluginsView = XDocument.Load(FindPluginsViewXamlPath()).Root;

        var actionBar = pluginsView!
            .Descendants()
            .Single(element => element.Name.LocalName == "WrapPanel"
                && AttributeValue(element, "Classes") == "PluginActionBar");

        var actionButtons = actionBar
            .Elements()
            .Where(element => element.Name.LocalName == "Button")
            .ToArray();

        actionButtons.Should().NotBeEmpty();
        actionButtons.Should().OnlyContain(element =>
            AttributeValue(element, "Classes") == "IconButton");
    }

    [Fact]
    public void PluginCards_ReserveEnoughHeightForHealthAndActionContent()
    {
        var pluginsView = XDocument.Load(FindPluginsViewXamlPath()).Root;

        var cardStyles = pluginsView!
            .Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .Where(element =>
                AttributeValue(element, "Selector") is "Border.PluginCard" or "Border.PluginCardInactive")
            .ToArray();

        cardStyles.Should().HaveCount(2);
        foreach (var style in cardStyles)
        {
            var minHeight = style
                .Elements()
                .Single(element => element.Name.LocalName == "Setter"
                    && AttributeValue(element, "Property") == "MinHeight")
                .Attribute("Value")!
                .Value;

            int.Parse(minHeight).Should().BeGreaterThanOrEqualTo(300);
        }
    }

    private static string? AttributeValue(XElement element, string attributeName)
    {
        return element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == attributeName)?.Value;
    }

    private static string FindPluginsViewXamlPath()
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
                "PluginsView.axaml");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate PluginsView.axaml from the test output directory.");
    }
}
