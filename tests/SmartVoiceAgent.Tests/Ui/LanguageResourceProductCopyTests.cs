using FluentAssertions;
using System.Xml.Linq;

namespace SmartVoiceAgent.Tests.Ui;

public sealed class LanguageResourceProductCopyTests
{
    [Fact]
    public void LanguageResources_UseProductReadyRuntimeCopy()
    {
        var languageFiles = Directory.GetFiles(FindLanguageResourceDirectory(), "*.axaml");

        languageFiles.Should().NotBeEmpty();

        foreach (var file in languageFiles)
        {
            var resources = LoadStringResources(file);

            resources["Lang.AppName"].Should().Be("Kam", file);
            resources["Lang.Settings.KernelVersionVal"].Should().Contain("v1.0.0", file);

            resources.Values.Should().NotContain(value =>
                value.Contains("KAM NEURAL CORE", StringComparison.OrdinalIgnoreCase)
                || value.Contains("KERNEL v3.5", StringComparison.OrdinalIgnoreCase)
                || value.Contains("v3.5.0", StringComparison.OrdinalIgnoreCase)
                || value.Contains("NEURAL LINK", StringComparison.OrdinalIgnoreCase)
                || value.Contains("NÖRAL", StringComparison.OrdinalIgnoreCase)
                || value.Contains("ÇEKİRDEK", StringComparison.OrdinalIgnoreCase)
                || value.Contains("KOORDİNATÖR", StringComparison.OrdinalIgnoreCase),
                file);
        }
    }

    [Fact]
    public void EnglishAndTurkishResources_RenameCoordinatorSurface()
    {
        var languageDirectory = FindLanguageResourceDirectory();
        var en = LoadStringResources(Path.Combine(languageDirectory, "en-US.axaml"));
        var tr = LoadStringResources(Path.Combine(languageDirectory, "tr-TR.axaml"));

        en["Lang.Coordinator.Title"].Should().Be("COMMAND CENTER");
        en["Lang.Coordinator.Subtitle"].Should().Be("AGENT WORKSPACE READY");
        en["Lang.Settings.KernelVersion"].Should().Be("Runtime Version");

        tr["Lang.Coordinator.Title"].Should().Be("KOMUT MERKEZI");
        tr["Lang.Coordinator.Subtitle"].Should().Be("AGENT CALISMA ALANI HAZIR");
        tr["Lang.Settings.KernelVersion"].Should().Be("Runtime Surumu");
    }

    private static IReadOnlyDictionary<string, string> LoadStringResources(string path)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        return XDocument.Load(path)
            .Descendants()
            .Where(element => element.Name.LocalName == "String")
            .Select(element => new
            {
                Key = element.Attribute(x + "Key")?.Value,
                Value = element.Value
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToDictionary(item => item.Key!, item => item.Value);
    }

    private static string FindLanguageResourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Ui",
                "SmartVoiceAgent.Ui",
                "Assets",
                "Lang");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate language resources from the test output directory.");
    }
}
