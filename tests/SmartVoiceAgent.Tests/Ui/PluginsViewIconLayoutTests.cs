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
            .Single(element => element.Name.LocalName == "StackPanel"
                && AttributeValue(element, "Classes") == "PluginActionBar");

        var actionButtons = actionBar
            .Elements()
            .Where(element => element.Name.LocalName == "Button")
            .ToArray();

        actionButtons.Should().NotBeEmpty();
        actionButtons.Should().OnlyContain(element =>
            AttributeValue(element, "Classes") == "IconButton");
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
