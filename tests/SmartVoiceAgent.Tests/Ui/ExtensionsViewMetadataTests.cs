using FluentAssertions;
using System.Xml.Linq;

namespace SmartVoiceAgent.Tests.Ui;

public sealed class ExtensionsViewMetadataTests
{
    [Fact]
    public void ExtensionsView_HasInstalledAndDiscoverTabsWithSearch()
    {
        var text = LocalizedXaml.ReadAllText(FindViewPath());

        text.Should().Contain("Text=\"Installed\"");
        text.Should().Contain("Text=\"Discover\"");
        text.Should().Contain("Command=\"{Binding ShowInstalledCommand}\"");
        text.Should().Contain("Command=\"{Binding ShowDiscoverCommand}\"");
        text.Should().Contain("Classes.Selected=\"{Binding IsDiscoverTab}\"");
        text.Should().Contain("Text=\"{Binding SearchText}\"");
        text.Should().Contain("PlaceholderText=\"Search extensions\"");
        text.Should().Contain("IsVisible=\"{Binding IsInstalledTab}\"");
        text.Should().Contain("IsVisible=\"{Binding IsDiscoverTab}\"");
    }

    [Fact]
    public void ExtensionsView_InstalledTab_GroupsEachKindBehindFilterChips()
    {
        var text = LocalizedXaml.ReadAllText(FindViewPath());

        foreach (var (title, filter, items) in new[]
        {
            ("MCP servers", "McpServers", "McpServers"),
            ("Plugins", "Plugins", "Plugins"),
            ("Skills", "Skills", "Skills"),
            ("Slash commands", "Commands", "Commands")
        })
        {
            text.Should().Contain($"Text=\"{title}\"");
            text.Should().Contain($"CommandParameter=\"{filter}\"");
            text.Should().Contain($"ItemsSource=\"{{Binding {items}}}\"");
        }

        text.Should().Contain("CommandParameter=\"All\"");
        text.Should().Contain("IsVisible=\"{Binding HasNoInstalledMatches}\"");
        text.Should().Contain("Text=\"Browse Discover\"");
    }

    [Fact]
    public void ExtensionsView_ExposesManagementActions()
    {
        var text = LocalizedXaml.ReadAllText(FindViewPath());

        foreach (var command in new[]
        {
            "OpenMcpConfigCommand", "ConnectServersCommand", "RestartCommand", "RemoveCommand", "OpenConfigCommand",
            "InstallPluginCommand", "OpenPluginsFolderCommand", "UninstallCommand", "OpenFolderCommand",
            "InstallSkillCommand", "BrowseSkillFolderCommand", "OpenSkillsFolderCommand", "OpenCommandsFolderCommand",
            "OpenFileCommand", "RefreshCommand", "SetFilterCommand", "AddCommand", "GetRuntimeCommand", "OpenHomepageCommand"
        })
        {
            text.Should().Contain($"{{Binding {command}}}");
        }

        text.Should().Contain("IsChecked=\"{Binding IsEnabled}\"");
        text.Should().Contain("IsVisible=\"{Binding CanToggle}\"");
        text.Should().Contain("<MenuFlyout");
    }

    [Fact]
    public void ExtensionsView_DiscoverTab_ShowsCatalogCardsThatStackWhenNarrow()
    {
        var text = LocalizedXaml.ReadAllText(FindViewPath());

        text.Should().Contain("ItemsSource=\"{Binding CatalogServers}\"");
        text.Should().Contain("ItemsSource=\"{Binding CatalogPlugins}\"");
        text.Should().Contain("Text=\"Popular MCP servers\"");
        text.Should().Contain("Text=\"Add\"");
        text.Should().Contain("Text=\"Added\"");
        text.Should().Contain("Converter={StaticResource IconResource}");
        text.Should().Contain("<UniformGrid Classes=\"CatalogGrid\"");
        text.Should().Contain("Selector=\"StackPanel.narrow UniformGrid.CatalogGrid\"");
        text.Should().Contain("Selector=\"StackPanel.narrow TextBox.ExtensionSearch\"");
    }

    [Fact]
    public void ExtensionsView_SearchHidesWholeRows()
    {
        var view = XDocument.Parse(LocalizedXaml.ReadAllText(FindViewPath())).Root!;

        var filtered = view.Descendants()
            .Where(element => element.Name.LocalName == "ItemsControl" && element.Attribute("Classes")?.Value == "Filtered")
            .Select(element => element.Attribute("ItemsSource")!.Value)
            .ToList();

        filtered.Should().Equal(
            "{Binding McpServers}", "{Binding Plugins}", "{Binding Skills}", "{Binding Commands}",
            "{Binding CatalogServers}", "{Binding CatalogPlugins}");
        var style = view.Descendants().Single(element => element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value == "ItemsControl.Filtered > ContentPresenter");
        style.Elements().Single().Attribute("Value")!.Value.Should().Be("{Binding IsVisible}");
    }

    [Fact]
    public void ExtensionsView_ServerStatusUsesColoredDot()
    {
        var view = XDocument.Parse(LocalizedXaml.ReadAllText(FindViewPath())).Root!;

        var dot = view.Descendants()
            .Single(element => element.Name.LocalName == "Border"
                && element.Attributes().Any(attribute => attribute.Name.LocalName == "Classes.Danger"));

        dot.Attribute("Classes")!.Value.Should().Be("StatusDot");
        dot.Attribute("Classes.Success")!.Value.Should().Be("{Binding IsReady}");
        dot.Attribute("Classes.Accent")!.Value.Should().Be("{Binding IsStarting}");
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
