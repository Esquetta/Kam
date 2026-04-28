using FluentAssertions;
using System.Xml.Linq;

namespace SmartVoiceAgent.Tests.Ui;

public sealed class CoordinatorViewMetadataTests
{
    [Fact]
    public void KernelBadge_DoesNotFadeTextWithParentOpacity()
    {
        var view = XDocument.Load(FindCoordinatorViewXamlPath()).Root;

        var kernelBadge = view!
            .Descendants()
            .First(element =>
                element.Name.LocalName == "Border"
                && element.Descendants().Any(child =>
                    child.Name.LocalName == "TextBlock"
                    && child.Attribute("Text")?.Value == "KERNEL v3.5"));

        kernelBadge.Attribute("Opacity").Should().BeNull();
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
