using FluentAssertions;
using System.Xml.Linq;

namespace SmartVoiceAgent.Tests.Ui;

public sealed class PluginsViewIconLayoutTests
{
    [Fact]
    public void SkillsPage_ListsSkillsAsSearchableGroupedRows()
    {
        var text = LocalizedXaml.ReadAllText(FindPluginsViewXamlPath());

        text.Should().Contain("Text=\"{Binding SearchText}\"");
        text.Should().Contain("PlaceholderText=\"Search skills\"");
        foreach (var filter in new[] { "All", "On", "Attention", "Off" })
        {
            text.Should().Contain($"CommandParameter=\"{filter}\"");
        }

        text.Should().Contain("ItemsSource=\"{Binding Groups}\"");
        text.Should().Contain("ItemsSource=\"{Binding Items}\"");
        text.Should().Contain("<Border Classes=\"ListRow\" Classes.Selected=\"{Binding IsSelected}\">");
        text.Should().Contain("Data=\"{Binding IconKey, Converter={StaticResource IconResource}}\"");
        text.Should().NotContain("PluginCard", "skills are compact rows now, not tall cards");
    }

    [Fact]
    public void SkillsPage_SearchHidesWholeRowsAndGroups()
    {
        var view = XDocument.Parse(LocalizedXaml.ReadAllText(FindPluginsViewXamlPath())).Root!;

        foreach (var selector in new[] { "ItemsControl.SkillRows > ContentPresenter", "ItemsControl.SkillGroups > ContentPresenter" })
        {
            var style = StyleFor(view, selector);
            style.Elements().Single().Attribute("Value")!.Value.Should().Be("{Binding IsVisible}");
        }

        view.Descendants()
            .Where(element => element.Name.LocalName == "ItemsControl")
            .Select(element => AttributeValue(element, "Classes"))
            .Should().Contain(["SkillGroups", "SkillRows"]);
    }

    [Fact]
    public void SkillRows_ShowStatusSwitchQuickFixAndExpandableDetails()
    {
        var text = LocalizedXaml.ReadAllText(FindPluginsViewXamlPath());

        text.Should().Contain("Classes.Warning=\"{Binding NeedsAttention}\"");
        text.Should().Contain("IsChecked=\"{Binding IsOn}\"");
        text.Should().Contain("IsVisible=\"{Binding CanToggle}\"");
        text.Should().Contain("Command=\"{Binding ApproveReviewCommand}\"");
        text.Should().Contain("Command=\"{Binding GrantPermissionsCommand}\"");
        text.Should().Contain("<StackPanel IsVisible=\"{Binding IsSelected}\"");
        text.Should().Contain("Command=\"{Binding TestSkillCommand}\"");
        text.Should().Contain("Command=\"{Binding RevokePermissionsCommand}\"");
        text.Should().Contain("ItemsSource=\"{Binding ExecutionHistory}\"");
        text.Should().Contain("$parent[UserControl].((vm:PluginsViewModel)DataContext).SaveRuntimePolicyOptionCommand");
    }

    [Fact]
    public void SkillsPage_KeepsEvalsAndImportCompact()
    {
        var text = LocalizedXaml.ReadAllText(FindPluginsViewXamlPath());

        text.Should().Contain("Command=\"{Binding RunSkillEvalCommand}\"");
        text.Should().Contain("Command=\"{Binding ToggleEvalResultsCommand}\"");
        text.Should().Contain("IsVisible=\"{Binding ShowEvalResults}\"");
        text.Should().Contain("Command=\"{Binding BrowseImportFolderCommand}\"");
        text.Should().Contain("Command=\"{Binding ImportSkillCommand}\"");
    }

    [Fact]
    public void SkillsPage_AdaptsToNarrowWidths()
    {
        var text = LocalizedXaml.ReadAllText(FindPluginsViewXamlPath());

        text.Should().Contain("x:Name=\"Page\"");
        foreach (var selector in new[]
        {
            "StackPanel.narrow TextBox.SkillSearch",
            "StackPanel.narrow StackPanel.EvalActions",
            "StackPanel.narrow ComboBox.ImportSource",
            "StackPanel.narrow TextBox.ImportPath",
            "StackPanel.narrow TextBlock.StatusText"
        })
        {
            text.Should().Contain($"Selector=\"{selector}\"");
        }

        File.ReadAllText(FindRepoFile("src", "Ui", "SmartVoiceAgent.Ui", "Views", "PluginsView.axaml.cs"))
            .Should().Contain("Page.Classes.Set(\"narrow\"");
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
