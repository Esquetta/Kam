using FluentAssertions;
using System.Xml.Linq;

namespace SmartVoiceAgent.Tests.Ui;

public sealed class IntegrationsViewMetadataTests
{
    private static readonly string ViewPath = Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "..",
        "src",
        "Ui",
        "SmartVoiceAgent.Ui",
        "Views",
        "IntegrationsView.axaml");

    [Fact]
    public void IntegrationsView_ShouldExposeGitHubAppConfigurationCard()
    {
        var xaml = LocalizedXaml.ReadAllText(Path.GetFullPath(ViewPath));

        xaml.Should().Contain("GitHub");
        xaml.Should().Contain("Connect GitHub");
        xaml.Should().Contain("ConnectGitHubCommand");
        xaml.Should().Contain("Advanced GitHub App");
        xaml.Should().Contain("ShowGitHubAppAdvancedSettings");
        xaml.Should().Contain("GitHubAppId");
        xaml.Should().Contain("GitHubInstallationId");
        xaml.Should().Contain("GitHubPrivateKeyPath");
        xaml.Should().Contain("SaveGitHubAppCommand");
        xaml.Should().Contain("ClearGitHubAppCommand");
    }

    [Fact]
    public void IntegrationsView_ShouldExposeWebSearchCard()
    {
        var xaml = LocalizedXaml.ReadAllText(Path.GetFullPath(ViewPath));

        xaml.Should().Contain("Text=\"Web search\"");
        xaml.Should().Contain("Lets the agent search the web. No key needed · Google optional");
        xaml.Should().Contain("Text=\"{Binding WebSearchApiKey, Mode=TwoWay}\"");
        xaml.Should().Contain("Text=\"{Binding WebSearchEngineId, Mode=TwoWay}\"");
        xaml.Should().Contain("SaveWebSearchCommand");
        xaml.Should().Contain("ClearWebSearchCommand");
        xaml.Should().Contain("WebSearchStatusText");
    }

    [Fact]
    public void IntegrationsView_ShouldExposeCodingAgentCliCard()
    {
        var xaml = LocalizedXaml.ReadAllText(Path.GetFullPath(ViewPath));

        xaml.Should().Contain("Text=\"Coding agent CLIs\"");
        xaml.Should().Contain("DataContext=\"{Binding CliAgents}\"");
        xaml.Should().Contain("ItemsSource=\"{Binding Agents}\"");
        xaml.Should().Contain("IsChecked=\"{Binding IsEnabled, Mode=TwoWay}\"");
        xaml.Should().Contain("IsEnabled=\"{Binding CanToggle}\"");
        xaml.Should().Contain("AutomationProperties.Name=\"{Binding Name}\"", "screen readers name each switch after its CLI");
        xaml.Should().Contain("AutomationProperties.Name=\"Check again\"");
        xaml.Should().Contain("Text=\"{Binding VersionText}\"");
        xaml.Should().Contain("RefreshCommand");
        xaml.Should().Contain("it never reads the CLI");
    }

    [Fact]
    public void IntegrationsView_GitHubAppCard_ShouldNotAskForRawPrivateKeyMaterial()
    {
        var xaml = LocalizedXaml.ReadAllText(Path.GetFullPath(ViewPath));

        xaml.Should().NotContain("BEGIN PRIVATE KEY");
        xaml.Should().NotContain("RawPrivateKey");
        xaml.Should().Contain("Private Key Path");
        xaml.Should().Contain("never paste raw private key material");
    }

    [Fact]
    public void IntegrationsView_GitHubAppCard_ShouldExposeConnectionTestActions()
    {
        var xaml = LocalizedXaml.ReadAllText(Path.GetFullPath(ViewPath));

        xaml.Should().Contain("Test Connection");
        xaml.Should().Contain("TestGitHubAppConnectionCommand");
        xaml.Should().Contain("List Repositories");
        xaml.Should().Contain("ListGitHubAppRepositoriesCommand");
        xaml.Should().Contain("CanListGitHubAppRepositories");
        xaml.Should().Contain("GitHubConnectionStatusText");
        xaml.Should().Contain("GitHubConnectionDetailText");
        xaml.Should().Contain("GitHubRepositoryPreviewText");
    }

    [Fact]
    public void IntegrationsView_GitHubAppCard_ShouldExposeSetupChecklist()
    {
        var xaml = LocalizedXaml.ReadAllText(Path.GetFullPath(ViewPath));
        var view = XDocument.Parse(xaml).Root!;

        xaml.Should().Contain("Setup checklist");
        xaml.Should().Contain("GitHubAppSetupSteps");
        xaml.Should().Contain("HasGitHubAppSetupSteps");

        var githubCard = FindIntegrationPane(view, "GitHub");

        githubCard.Descendants()
            .Single(e => e.Name.LocalName == "StackPanel"
                && AttributeValue(e, "IsVisible") == "{Binding ShowGitHubAppAdvancedSettings}")
            .Should().NotBeNull();

        var checklistHost = githubCard.Descendants()
            .Single(e => e.Name.LocalName == "StackPanel"
                && AttributeValue(e, "IsVisible") == "{Binding HasGitHubAppSetupSteps}");

        checklistHost.Descendants()
            .Single(e => e.Name.LocalName == "TextBlock" && AttributeValue(e, "Text") == "Setup checklist")
            .Should().NotBeNull();

        var checklist = checklistHost.Descendants()
            .Single(e => e.Name.LocalName == "ItemsControl"
                && AttributeValue(e, "ItemsSource") == "{Binding GitHubAppSetupSteps}");

        var template = checklist.Descendants().Single(e => e.Name.LocalName == "DataTemplate");
        AttributeValue(template, "DataType").Should().Be("vm:RuntimeDiagnosticItemViewModel");

        var visibleBindings = template.Descendants().Select(e => AttributeValue(e, "IsVisible"));
        visibleBindings.Should().Contain("{Binding IsReady}");
        visibleBindings.Should().Contain("{Binding IsWarning}");
        visibleBindings.Should().Contain("{Binding IsBlocked}");

        var textBindings = template.Descendants().Select(e => AttributeValue(e, "Text"));
        textBindings.Should().Contain("{Binding Name}");
        textBindings.Should().Contain("{Binding Detail}");
        textBindings.Should().Contain("{Binding Value}");

        template.Descendants().Single(e =>
            e.Name.LocalName == "TextBlock"
            && AttributeValue(e, "Text") == "{Binding Value}"
            && AttributeValue(e, "MaxWidth") == "120"
            && AttributeValue(e, "TextAlignment") == "Right")
            .Should().NotBeNull();
    }

    [Fact]
    public void IntegrationsView_IsOnePanePerIntegrationWithAStatusDotAndNoIconTiles()
    {
        var view = XDocument.Parse(LocalizedXaml.ReadAllText(Path.GetFullPath(ViewPath))).Root!;

        var panes = view.Descendants()
            .Where(e => e.Name.LocalName == "Border" && AttributeValue(e, "Classes") == "Pane")
            .ToArray();
        panes.Select(pane => AttributeValue(pane.Descendants().First(e => AttributeValue(e, "Classes") == "SectionTitle"), "Text"))
            .Should().Equal("Todoist", "Web search", "Coding agent CLIs", "GitHub", "Email (SMTP)", "SMS (Twilio)");

        // The head holds the name, one sentence and an info tooltip; status is a dot and text, never a pill.
        panes.Should().OnlyContain(pane => pane.Descendants().Any(e => AttributeValue(e, "Classes") == "PaneHead"));
        panes.Should().OnlyContain(pane => pane.Descendants().Any(e => AttributeValue(e, "Classes") == "InfoTip"));
        view.Descendants().Should().NotContain(e => AttributeValue(e, "Classes") == "IconBadge" || AttributeValue(e, "Classes") == "Pill");
        view.Descendants().Where(e => AttributeValue(e, "Classes") == "PaneStatus").Should().HaveCount(6);

        // Inputs take the shared 32 px height and every field has a label above it.
        view.Descendants().Where(e => e.Name.LocalName == "TextBox")
            .Should().OnlyContain(box => AttributeValue(box, "Height") == string.Empty)
            .And.OnlyContain(box => AttributeValue(box.Parent!, "Classes") == "Field");
    }

    [Theory]
    [InlineData("SaveTodoistCommand", "IsTodoistDirty", "ClearTodoistCommand", "HasSavedTodoist")]
    [InlineData("SaveWebSearchCommand", "IsWebSearchDirty", "ClearWebSearchCommand", "IsWebSearchConfigured")]
    [InlineData("SaveGitHubAppCommand", "IsGitHubAppDirty", "ClearGitHubAppCommand", "HasSavedGitHubApp")]
    [InlineData("SaveEmailCommand", "IsEmailDirty", "ClearEmailCommand", "HasSavedEmail")]
    [InlineData("SaveSmsCommand", "IsSmsDirty", "ClearSmsCommand", "HasSavedSms")]
    public void IntegrationsView_SaveIsPrimaryOnlyWhileDirtyAndClearShowsOnlyWhenSaved(
        string saveCommand, string dirty, string clearCommand, string saved)
    {
        var view = XDocument.Parse(LocalizedXaml.ReadAllText(Path.GetFullPath(ViewPath))).Root!;

        var save = view.Descendants().Single(e => AttributeValue(e, "Command") == $"{{Binding {saveCommand}}}");
        AttributeValue(save, "Classes.PrimaryAction").Should().Be($"{{Binding {dirty}}}");
        AttributeValue(save, "Classes.SecondaryAction").Should().Be($"{{Binding !{dirty}}}");
        AttributeValue(save, "Content").Should().Be("Save");
        save.Elements().Should().BeEmpty("Save and Clear carry no icons");

        var clear = view.Descendants().Single(e => AttributeValue(e, "Command") == $"{{Binding {clearCommand}}}");
        AttributeValue(clear, "Classes").Should().Be("GhostAction Sm");
        AttributeValue(clear, "IsVisible").Should().Be($"{{Binding {saved}}}");
        AttributeValue(clear, "Content").Should().Be("Clear");
        clear.ElementsAfterSelf().Should().Contain(save, "Clear sits left of Save");
    }

    private static XElement FindIntegrationPane(XElement root, string title)
    {
        return root.Descendants()
            .Where(e => e.Name.LocalName == "Border" && AttributeValue(e, "Classes") == "Pane")
            .Single(pane => pane.Descendants().Any(e =>
                e.Name.LocalName == "TextBlock" && AttributeValue(e, "Text") == title));
    }

    private static string AttributeValue(XElement element, string name)
    {
        return element.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == name)?.Value ?? string.Empty;
    }
}
