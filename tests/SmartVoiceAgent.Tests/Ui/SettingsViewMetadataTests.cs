using FluentAssertions;
using System.Xml.Linq;

namespace SmartVoiceAgent.Tests.Ui;

public sealed class SettingsViewMetadataTests
{
    [Fact]
    public void SettingsView_DoesNotExposeModelProviderEndpoints()
    {
        var view = XDocument.Load(FindSettingsViewXamlPath()).Root;

        view.Should().NotBeNull();
        view!
            .Descendants()
            .Where(element => element.Name.LocalName is "TextBlock" or "TextBox")
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .Should()
            .NotContain(value =>
                value.Contains("Endpoint", StringComparison.OrdinalIgnoreCase)
                || value.Contains("AiEndpoint", StringComparison.Ordinal)
                || value.Contains("ChatEndpoint", StringComparison.Ordinal));
    }

    [Fact]
    public void SettingsView_PlannerConnectionButtonAlignsWithApiKeyInput()
    {
        var view = XDocument.Load(FindSettingsViewXamlPath()).Root;

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

    private static string FindSettingsViewXamlPath()
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
