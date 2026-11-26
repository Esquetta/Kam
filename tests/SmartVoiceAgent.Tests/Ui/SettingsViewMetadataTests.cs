using FluentAssertions;
using SmartVoiceAgent.Ui.Services;
using System.Xml.Linq;

namespace SmartVoiceAgent.Tests.Ui;

public sealed class SettingsViewMetadataTests
{
    [Fact]
    public void SettingsView_DoesNotExposeModelProviderEndpoints()
    {
        var view = XDocument.Parse(LocalizedXaml.ReadAllText(FindSettingsViewXamlPath())).Root;

        view.Should().NotBeNull();
        var voiceSection = FindVoiceSection(view!);
        var textElements = view!
            .Descendants()
            .Where(element => element.Name.LocalName is "TextBlock" or "TextBox")
            .ToList();

        textElements
            .Where(element => !element.Ancestors().Contains(voiceSection))
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .Should()
            .NotContain(value => value.Contains("Endpoint", StringComparison.OrdinalIgnoreCase));
        textElements
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .Should()
            .NotContain(value =>
                value.Contains("AiEndpoint", StringComparison.Ordinal)
                || value.Contains("ChatEndpoint", StringComparison.Ordinal));

        // The only address on the page is the speech API's, shown when that engine is chosen.
        textElements
            .Where(element => element.Attributes().Any(attribute =>
                attribute.Value.Contains("Endpoint", StringComparison.OrdinalIgnoreCase)))
            .Should()
            .ContainSingle()
            .Which.Attribute("Text")?.Value.Should().Be("{Binding SpeechApiEndpoint, Mode=TwoWay}");
    }

    [Fact]
    public void SettingsView_PlannerConnectionButtonAlignsWithApiKeyInput()
    {
        var view = XDocument.Parse(LocalizedXaml.ReadAllText(FindSettingsViewXamlPath())).Root;

        var testConnectionButton = view!
            .Descendants()
            .First(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding TestAiConnectionCommand}");

        testConnectionButton.Parent!.Name.LocalName.Should().Be("Grid");
        testConnectionButton.Attribute(XName.Get("Row", "https://github.com/avaloniaui"))?.Value.Should().Be("1");
        testConnectionButton.Attribute(XName.Get("Column", "https://github.com/avaloniaui"))?.Value.Should().Be("1");
        testConnectionButton.Attribute("VerticalAlignment")?.Value.Should().Be("Stretch");
    }

    [Fact]
    public void SettingsView_HasASubNavigationThatShowsOneSectionAtATime()
    {
        var view = XDocument.Parse(LocalizedXaml.ReadAllText(FindSettingsViewXamlPath())).Root!;

        var nav = view.Descendants().Single(element => element.Attribute("Classes")?.Value == "SectionNav");
        var items = nav.Elements().ToList();
        items.Select(item => item.Attribute("Content")!.Value).Should().Equal("Model", "Appearance", "Voice", "Startup", "Advanced");
        items.Should().OnlyContain(item => item.Name.LocalName == "Button"
            && item.Attribute("Classes")!.Value == "SectionNavItem"
            && item.Attribute("Command")!.Value == "{Binding SelectSectionCommand}");
        items.Select(item => item.Attribute("CommandParameter")!.Value).Should().Equal(
            "{x:Static vm:SettingsSection.Model}", "{x:Static vm:SettingsSection.Appearance}", "{x:Static vm:SettingsSection.Voice}",
            "{x:Static vm:SettingsSection.Startup}", "{x:Static vm:SettingsSection.Advanced}");
        items.Select(item => item.Attribute("Classes.Selected")!.Value).Should().Equal(
            "{Binding IsModelSectionSelected}", "{Binding IsAppearanceSectionSelected}", "{Binding IsVoiceSectionSelected}",
            "{Binding IsStartupSectionSelected}", "{Binding IsAdvancedSectionSelected}");

        // Each section is one panel of the content column, shown only while selected.
        var content = view.Descendants().Single(element => element.Attribute("Classes")?.Value == "SectionContent");
        content.Attribute("MaxWidth")!.Value.Should().Be("640");
        content.Elements().Select(section => section.Attribute("IsVisible")!.Value).Should().Equal(
            "{Binding IsModelSectionSelected}", "{Binding IsAppearanceSectionSelected}", "{Binding IsVoiceSectionSelected}",
            "{Binding IsStartupSectionSelected}", "{Binding IsAdvancedSectionSelected}");

        // Sentence-case section titles, Panes of rows, no icon tiles or overlines.
        view.Descendants().Should().NotContain(element => element.Attribute("Classes") != null
            && (element.Attribute("Classes")!.Value.Contains("IconBadge") || element.Attribute("Classes")!.Value.Contains("Overline")));
        view.Descendants().Where(element => element.Attribute("Classes")?.Value == "SectionTitle")
            .Select(title => title.Attribute("Text")!.Value)
            .Should().OnlyContain(text => text != text.ToUpperInvariant());

        // Diagnostics is a link under Advanced.
        var diagnostics = view.Descendants().Single(element =>
            element.Attribute("Command")?.Value.Contains("NavigateToDiagnosticsCommand", StringComparison.Ordinal) == true);
        diagnostics.Attribute("Classes")!.Value.Should().Be("LinkAction");
        diagnostics.Ancestors().Should().Contain(element => (string?)element.Attribute("IsVisible") == "{Binding IsAdvancedSectionSelected}");
    }

    [Fact]
    public void SettingsView_ModelSectionHasNoMaskedKeyCopyAndKeepsThePlannerCollapsed()
    {
        var view = XDocument.Parse(LocalizedXaml.ReadAllText(FindSettingsViewXamlPath())).Root!;

        view.Descendants().Should().NotContain(element => element.Attributes().Any(attribute =>
            attribute.Value.Contains("MaskedChatApiKey", StringComparison.Ordinal)
            || attribute.Value.Contains("MaskedAiApiKey", StringComparison.Ordinal)));
        view.Descendants()
            .Where(element => element.Name.LocalName == "TextBox" && element.Attribute("PasswordChar") != null)
            .Should().HaveCount(3)
            .And.OnlyContain(box => box.Attribute("Classes")!.Value == "revealPasswordButton");

        var refresh = view.Descendants().Single(element => element.Attribute("Command")?.Value == "{Binding RefreshChatModelsCommand}");
        refresh.Attribute("Classes")!.Value.Should().Be("IconButton");
        refresh.Attribute("AutomationProperties.Name")!.Value.Should().Be("Refresh");

        var planner = view.Descendants().Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "PlannerToggle");
        planner.Attribute("Classes")!.Value.Should().Be("Disclosure");
        planner.Attribute("IsChecked").Should().BeNull("the planner starts collapsed");
        planner.Descendants().Should().Contain(element => (string?)element.Attribute("Text") == "Advanced");
        view.Descendants().Single(element => element.Attribute("IsVisible")?.Value == "{Binding #PlannerToggle.IsChecked}")
            .Descendants().Should().Contain(element => (string?)element.Attribute("SelectedItem") == "{Binding AiProvider, Mode=TwoWay}");
    }

    [Fact]
    public void SettingsView_AccentSwatchesAreSmallCirclesNamedInTheirTooltip()
    {
        var view = XDocument.Parse(File.ReadAllText(FindSettingsViewXamlPath())).Root!;

        var swatch = view.Descendants().Single(element => element.Attribute("Classes")?.Value == "AccentSwatch");
        swatch.Attribute("AutomationProperties.Name")!.Value.Should().Be("{Binding Name}");
        swatch.Attribute("ToolTip.Tip")!.Value.Should().Be("{Binding Name}");
        swatch.Descendants().Should().NotContain(element => element.Name.LocalName == "TextBlock", "the name lives in the tooltip");
        var circle = swatch.Descendants().Single(element => element.Name.LocalName == "Ellipse");
        circle.Attribute("Width")!.Value.Should().Be("20");
        circle.Attribute("Height")!.Value.Should().Be("20");

        var selectedRing = view.Descendants().Single(element => element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value == "Button.AccentSwatch.Selected Border.SwatchRing");
        selectedRing.Elements().Single().Attribute("Value")!.Value.Should().Be("{DynamicResource TextPrimaryBrush}");
    }

    [Fact]
    public void SettingsView_ReducedMotionSaysItTurnsOffAnimations()
    {
        var xaml = LocalizedXaml.ReadAllText(FindSettingsViewXamlPath());

        xaml.Should().Contain("Text=\"Turn off animations\"");
        xaml.Should().NotContain("particle", "the setting turns off animations, there are no particle effects");
        LocalizationService.LoadDictionary("tr-TR")["Settings.ReducedMotionDesc"].Should().Be("Animasyonları kapat");
    }

    /// <summary>
    /// Returns the panel that follows the <c>Voice Settings Section</c> comment.
    /// </summary>
    /// <param name="view">The parsed view.</param>
    internal static XElement FindVoiceSection(XElement view)
    {
        var comment = view
            .DescendantNodes()
            .OfType<XComment>()
            .Single(node => node.Value.Trim() == "Voice Settings Section");

        return comment.ElementsAfterSelf().First();
    }

    internal static string FindSettingsViewXamlPath()
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
                "SettingsView.axaml");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate SettingsView.axaml from the test output directory.");
    }
}
