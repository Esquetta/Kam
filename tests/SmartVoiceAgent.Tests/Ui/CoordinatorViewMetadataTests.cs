using FluentAssertions;
using System.Xml.Linq;

namespace SmartVoiceAgent.Tests.Ui;

public sealed class CoordinatorViewMetadataTests
{
    [Fact]
    public void RuntimeBadge_DoesNotFadeTextWithParentOpacity()
    {
        var view = XDocument.Load(FindCoordinatorViewXamlPath()).Root;

        var runtimeBadge = view!
            .Descendants()
            .First(element =>
                element.Name.LocalName == "Border"
                && element.Descendants().Any(child =>
                    child.Name.LocalName == "TextBlock"
                    && child.Attribute("Text")?.Value == "AGENT v1.0"));

        runtimeBadge.Attribute("Opacity").Should().BeNull();
    }

    [Fact]
    public void CoordinatorView_DoesNotRenderLegacyProductCopy()
    {
        var view = XDocument.Load(FindCoordinatorViewXamlPath()).Root;

        var visibleText = view!
            .Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .Where(value => !value.StartsWith('{'));

        visibleText.Should().NotContain(value =>
            value.Contains("KERNEL", StringComparison.OrdinalIgnoreCase)
            || value.Contains("NEURAL", StringComparison.OrdinalIgnoreCase)
            || value.Contains("COORDINATOR", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindCoordinatorViewXamlPath()
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
                "CoordinatorView.axaml");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate CoordinatorView.axaml from the test output directory.");
    }
}
