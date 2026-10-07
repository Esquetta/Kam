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
