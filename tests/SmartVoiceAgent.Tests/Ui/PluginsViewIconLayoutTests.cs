using FluentAssertions;
using System.Xml.Linq;

namespace SmartVoiceAgent.Tests.Ui;

public sealed class PluginsViewIconLayoutTests
{
    [Fact]
    public void PluginCards_CenterStatusGlyphsInsideIconBadge()
    {
        var pluginsView = XDocument.Parse(LocalizedXaml.ReadAllText(FindPluginsViewXamlPath())).Root;

        var iconBadge = pluginsView!
            .Descendants()
            .Single(element => element.Name.LocalName == "Grid"
                && AttributeValue(element, "Classes") == "PluginIconBadge");

        var stateGlyphs = iconBadge
            .Elements()
            .Where(element => element.Name.LocalName == "Path"
                && AttributeValue(element, "Data") == "{Binding IconPath}"
                && AttributeValue(element, "Width") == "18"
                && AttributeValue(element, "Height") == "18")
            .ToArray();

        stateGlyphs.Should().HaveCount(2);
        stateGlyphs.Should().OnlyContain(element =>
            AttributeValue(element, "HorizontalAlignment") == "Center"
            && AttributeValue(element, "VerticalAlignment") == "Center"
            && AttributeValue(element, "Stretch") == "Uniform");
    }

    [Fact]
    public void PluginCards_UseSharedIconButtonLayoutForActions()
    {
        var pluginsView = XDocument.Parse(LocalizedXaml.ReadAllText(FindPluginsViewXamlPath())).Root;

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
            classes.Should().Contain("CompactIconButton");
        }

        actionButtons.Should().OnlyContain(element =>
            AttributeValue(element, "Width") == null
            && AttributeValue(element, "Height") == null
            && AttributeValue(element, "Padding") == "0"
            && AttributeValue(element, "Margin") == null);

        var actionGlyphs = actionButtons
            .SelectMany(element => element.Elements())
            .ToArray();

        actionGlyphs.Should().HaveCount(actionButtons.Length);
        actionGlyphs.Should().OnlyContain(element =>
            element.Name.LocalName == "Viewbox"
            && AttributeValue(element, "Width") == "14"
            && AttributeValue(element, "Height") == "14");
        actionGlyphs
            .Select(element => element.Elements().Single())
            .Should()
            .OnlyContain(path => path.Name.LocalName == "Path"
                && (AttributeValue(path, "Classes") ?? string.Empty).Split(' ', StringSplitOptions.None).Contains("Icon")
                && (AttributeValue(path, "Data") ?? string.Empty).StartsWith("{StaticResource Icon", StringComparison.Ordinal));
    }

    [Fact]
    public void PluginCards_KeepActionButtonsInStableFooter()
    {
        var pluginsView = XDocument.Parse(LocalizedXaml.ReadAllText(FindPluginsViewXamlPath())).Root;

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
        var pluginsView = XDocument.Parse(LocalizedXaml.ReadAllText(FindPluginsViewXamlPath())).Root;

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
                && AttributeValue(element, "FontSize") == "15"
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

        var compactIconButtonStyle = controls
            .Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && AttributeValue(element, "Selector") == "Button.CompactIconButton");

        var compactSetters = compactIconButtonStyle
            .Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(element => AttributeValue(element, "Property")!, element => AttributeValue(element, "Value"));

        compactSetters["Width"].Should().Be("24");
        compactSetters["Height"].Should().Be("24");
        compactSetters["MinWidth"].Should().Be("24");
        compactSetters["MinHeight"].Should().Be("24");

        var presenterStyle = controls
            .Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && AttributeValue(element, "Selector") == "Button.IconButton /template/ ContentPresenter#PART_ContentPresenter, Button.IconAction /template/ ContentPresenter#PART_ContentPresenter");

        // The presenter fills the button, so the hover background covers the whole button rather than a blob around the glyph.
        Setters(presenterStyle)
            .Should()
            .Contain("HorizontalAlignment", "Stretch")
            .And.Contain("VerticalAlignment", "Stretch")
            .And.Contain("HorizontalContentAlignment", "Center")
            .And.Contain("VerticalContentAlignment", "Center");

        // Glyphs keep their 24x24 grid; Uniform would fit narrow glyphs to their bounds and push them off-center.
        var glyphStyle = controls
            .Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && AttributeValue(element, "Selector") == "Button.IconButton Path.Icon, Button.IconAction Path.Icon");

        Setters(glyphStyle).Should().Contain("Stretch", "None");
    }

    [Fact]
    public void IconButtons_HighlightTheWholeButtonAndTheGlyphOnHover()
    {
        var controls = XDocument.Load(FindControlsXamlPath()).Root!;

        Setters(StyleFor(controls, "Button.IconButton:pointerover /template/ ContentPresenter#PART_ContentPresenter, Button.IconAction:pointerover /template/ ContentPresenter#PART_ContentPresenter"))
            .Should().Contain("Background", "{DynamicResource ControlHoverBrush}");
        Setters(StyleFor(controls, "Button.IconButton:pressed /template/ ContentPresenter#PART_ContentPresenter, Button.IconAction:pressed /template/ ContentPresenter#PART_ContentPresenter"))
            .Should().Contain("Background", "{DynamicResource ControlPressedBrush}");
        Setters(StyleFor(controls, "Button.IconButton:pointerover Path.Icon, Button.IconAction:pointerover Path.Icon"))
            .Should().Contain("Stroke", "{DynamicResource TextPrimaryBrush}");

        var pluginsView = XDocument.Parse(LocalizedXaml.ReadAllText(FindPluginsViewXamlPath())).Root!;
        pluginsView.Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .Select(element => AttributeValue(element, "Selector") ?? string.Empty)
            .Should()
            .NotContain(selector => selector.Contains("IconButton", StringComparison.Ordinal) || selector.Contains("PluginActionButton", StringComparison.Ordinal),
                "a page style must not switch the shared icon button hover off");
    }

    [Theory]
    [InlineData("MainWindow.axaml")]
    [InlineData("PluginsView.axaml")]
    [InlineData("ExtensionsView.axaml")]
    [InlineData("SettingsView.axaml")]
    [InlineData("IntegrationsView.axaml")]
    [InlineData("RuntimeDiagnosticsView.axaml")]
    public void IconButtons_DrawStrokeGlyphsTheHoverStyleCanRecolor(string viewFileName)
    {
        var view = XDocument.Parse(LocalizedXaml.ReadAllText(FindRepoFile("src", "Ui", "SmartVoiceAgent.Ui", "Views", viewFileName))).Root!;

        var glyphs = view.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Where(element => (AttributeValue(element, "Classes") ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Intersect(["IconButton", "IconAction"])
                .Any())
            .SelectMany(button => button.Descendants().Where(element => element.Name.LocalName == "Path"))
            .ToArray();

        glyphs.Should().OnlyContain(path =>
            (AttributeValue(path, "Classes") ?? string.Empty).Split(' ', StringSplitOptions.None).Contains("Icon")
            && AttributeValue(path, "Fill") == null
            && AttributeValue(path, "Stroke") == null,
            "icon buttons use Path.Icon glyphs; a local Fill or Stroke would keep the glyph from brightening on hover");
    }

    [Fact]
    public void PluginCards_ReserveEnoughHeightForHealthAndActionContent()
    {
        var pluginsView = XDocument.Parse(LocalizedXaml.ReadAllText(FindPluginsViewXamlPath())).Root;

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

    private static XElement StyleFor(XElement root, string selector)
    {
        return root.Descendants()
            .Single(element => element.Name.LocalName == "Style" && AttributeValue(element, "Selector") == selector);
    }

    private static Dictionary<string, string?> Setters(XElement style)
    {
        return style.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(element => AttributeValue(element, "Property")!, element => AttributeValue(element, "Value"));
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
