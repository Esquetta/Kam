using FluentAssertions;
using System.Xml.Linq;

namespace SmartVoiceAgent.Tests.Ui;

public sealed class ExtensionsViewMetadataTests
{
    [Fact]
    public void ExtensionsView_HasSectionForEachExtensionKind()
    {
        var text = File.ReadAllText(FindViewPath());

        text.Should().Contain("Text=\"MCP servers\"");
        text.Should().Contain("Text=\"Plugins\"");
        text.Should().Contain("Text=\"Skills\"");
        text.Should().Contain("Text=\"Slash commands\"");
        text.Should().Contain("ItemsSource=\"{Binding McpServers}\"");
        text.Should().Contain("ItemsSource=\"{Binding Plugins}\"");
        text.Should().Contain("ItemsSource=\"{Binding Skills}\"");
        text.Should().Contain("ItemsSource=\"{Binding Commands}\"");
    }

    [Fact]
    public void ExtensionsView_ExposesManagementActions()
    {
        var text = File.ReadAllText(FindViewPath());

        foreach (var command in new[]
        {
            "OpenMcpConfigCommand", "ReloadServersCommand", "ConnectServersCommand", "RestartCommand",
            "InstallPluginCommand", "OpenPluginsFolderCommand", "UninstallCommand",
            "InstallSkillCommand", "OpenSkillsFolderCommand", "OpenCommandsFolderCommand", "RefreshCommand"
        })
        {
            text.Should().Contain($"{{Binding {command}}}");
        }

        text.Should().Contain("IsChecked=\"{Binding IsEnabled}\"");
    }

    [Fact]
    public void ExtensionsView_StatusPillsUseSharedPillVariants()
    {
        var view = XDocument.Load(FindViewPath()).Root!;

        var pill = view.Descendants()
            .Single(element => element.Name.LocalName == "Border"
                && element.Attributes().Any(attribute => attribute.Name.LocalName == "Classes.Danger"));

        pill.Attribute("Classes")!.Value.Should().Be("Pill");
        pill.Attribute("Classes.Success")!.Value.Should().Be("{Binding IsReady}");
        pill.Attribute("Classes.Accent")!.Value.Should().Be("{Binding IsStarting}");
    }

    private static string FindViewPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Ui", "SmartVoiceAgent.Ui", "Views", "ExtensionsView.axaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("ExtensionsView.axaml was not found.");
    }
}
