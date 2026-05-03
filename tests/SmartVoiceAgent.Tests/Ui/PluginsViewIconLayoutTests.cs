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
        foreach (var actionButton in actionButtons)
        {
            var classes = AttributeValue(actionButton, "Classes")?.Split(' ') ?? [];
            classes.Should().Contain("IconButton");
            classes.Should().Contain("PluginActionButton");
        }

        actionButtons.Should().OnlyContain(element =>
            AttributeValue(element, "Width") == "24"
            && AttributeValue(element, "Height") == "24"
            && AttributeValue(element, "Padding") == "0"
            && AttributeValue(element, "Margin") == null);

        var actionGlyphs = actionButtons
            .SelectMany(element => element.Descendants())
            .Where(element => element.Name.LocalName == "Path")
            .ToArray();

        actionGlyphs.Should().HaveCount(actionButtons.Length);
        actionGlyphs.Should().OnlyContain(element =>
            AttributeValue(element, "Width") == "16"
            && AttributeValue(element, "Height") == "16"
            && AttributeValue(element, "Stretch") == "Uniform"
            && AttributeValue(element, "HorizontalAlignment") == "Center"
            && AttributeValue(element, "VerticalAlignment") == "Center");
    }

    [Fact]
    public void PluginCards_KeepActionButtonsInStableFooter()
    {
        var pluginsView = XDocument.Load(FindPluginsViewXamlPath()).Root;

        var actionBar = pluginsView!
            .Descendants()
            .Single(element => element.Name.LocalName == "StackPanel"
                && AttributeValue(element, "Classes") == "PluginActionBar");

        actionBar.Parent!.Name.LocalName.Should().Be("Grid");
        AttributeValue(actionBar, "Grid.Row").Should().Be("2");
        AttributeValue(actionBar, "Orientation").Should().Be("Horizontal");
        AttributeValue(actionBar, "Spacing").Should().Be("8");
        AttributeValue(actionBar, "HorizontalAlignment").Should().Be("Left");
        AttributeValue(actionBar, "VerticalAlignment").Should().Be("Bottom");
    }

    [Fact]
    public void PluginCards_ConstrainLabelsForResponsiveCards()
    {
        var pluginsView = XDocument.Load(FindPluginsViewXamlPath()).Root;

        var statusBadge = pluginsView!
            .Descendants()
            .Single(element => element.Name.LocalName == "Border"
                && AttributeValue(element, "Classes.StatusActive") == "{Binding IsActive}"
                && AttributeValue(element, "Classes.StatusInactive") == "{Binding !IsActive}");

        AttributeValue(statusBadge, "MaxWidth").Should().Be("82");

        var statusText = statusBadge
            .Elements()
            .Single(element => element.Name.LocalName == "TextBlock"
                && AttributeValue(element, "Text") == "{Binding Status}");

        AttributeValue(statusText, "TextWrapping").Should().Be("NoWrap");
        AttributeValue(statusText, "TextTrimming").Should().Be("CharacterEllipsis");

        var nameText = pluginsView
            .Descendants()
            .Single(element => element.Name.LocalName == "TextBlock"
                && AttributeValue(element, "Text") == "{Binding Name}"
                && AttributeValue(element, "FontSize") == "16"
                && AttributeValue(element, "FontWeight") == "SemiBold");

        AttributeValue(nameText, "TextWrapping").Should().Be("Wrap");
        AttributeValue(nameText, "MaxLines").Should().Be("2");
        AttributeValue(nameText, "TextTrimming").Should().Be("CharacterEllipsis");
    }

    [Fact]
    public void SharedIconButtonStyle_CentersContentWithDeterministicMinimumSize()
    {
        var controls = XDocument.Load(FindControlsXamlPath()).Root;

        var iconButtonStyle = controls!
            .Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && AttributeValue(element, "Selector") == "Button.IconButton");

        var setters = iconButtonStyle
            .Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(element => AttributeValue(element, "Property")!, element => AttributeValue(element, "Value"));

        setters["MinWidth"].Should().Be("28");
        setters["MinHeight"].Should().Be("28");
        setters["Padding"].Should().Be("0");
        setters["HorizontalContentAlignment"].Should().Be("Center");
        setters["VerticalContentAlignment"].Should().Be("Center");

        var presenterStyle = controls
            .Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && AttributeValue(element, "Selector") == "Button.IconButton /template/ ContentPresenter#PART_ContentPresenter");

        presenterStyle
            .Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(element => AttributeValue(element, "Property")!, element => AttributeValue(element, "Value"))
            .Should()
            .Contain("HorizontalAlignment", "Center")
            .And.Contain("VerticalAlignment", "Center");
    }

    [Fact]
    public void PluginActionButtonStyle_RendersFlatAcrossInteractiveStates()
    {
        var pluginsView = XDocument.Load(FindPluginsViewXamlPath()).Root;

        var flatStyle = pluginsView!
            .Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && AttributeValue(element, "Selector") == "Button.PluginActionButton");

        var setters = flatStyle
            .Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(element => AttributeValue(element, "Property")!, element => AttributeValue(element, "Value"));

        setters["Background"].Should().Be("Transparent");
        setters["BorderBrush"].Should().Be("Transparent");
        setters["BorderThickness"].Should().Be("0");
        setters.Should().NotContainKey("BoxShadow");

        var hoverStyle = pluginsView
            .Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && AttributeValue(element, "Selector") == "Button.PluginActionButton:pointerover");

        hoverStyle
            .Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(element => AttributeValue(element, "Property")!, element => AttributeValue(element, "Value"))
            .Should()
            .Contain("Background", "Transparent")
            .And.Contain("BorderBrush", "Transparent")
            .And.Contain("BorderThickness", "0");

        var presenterStyle = pluginsView
            .Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && AttributeValue(element, "Selector") == "Button.PluginActionButton /template/ ContentPresenter#PART_ContentPresenter");

        presenterStyle
            .Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(element => AttributeValue(element, "Property")!, element => AttributeValue(element, "Value"))
            .Should()
            .Contain("Background", "Transparent")
            .And.Contain("BorderBrush", "Transparent")
            .And.Contain("BorderThickness", "0")
            .And.Contain("Padding", "0");
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
        return FindRepoFile("src", "Ui", "SmartVoiceAgent.Ui", "Views", "PluginsView.axaml");
    }

    private static string FindControlsXamlPath()
    {
        return FindRepoFile("src", "Ui", "SmartVoiceAgent.Ui", "Themes", "Controls.axaml");
    }

    private static string FindRepoFile(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(pathSegments)} from the test output directory.");
    }
}
