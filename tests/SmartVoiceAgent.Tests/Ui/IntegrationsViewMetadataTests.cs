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
        var xaml = File.ReadAllText(Path.GetFullPath(ViewPath));

        xaml.Should().Contain("GitHub App");
        xaml.Should().Contain("GitHubAppId");
        xaml.Should().Contain("GitHubInstallationId");
        xaml.Should().Contain("GitHubPrivateKeyPath");
        xaml.Should().Contain("SaveGitHubAppCommand");
        xaml.Should().Contain("ClearGitHubAppCommand");
    }

    [Fact]
    public void IntegrationsView_GitHubAppCard_ShouldNotAskForRawPrivateKeyMaterial()
    {
        var xaml = File.ReadAllText(Path.GetFullPath(ViewPath));

        xaml.Should().NotContain("BEGIN PRIVATE KEY");
        xaml.Should().NotContain("RawPrivateKey");
        xaml.Should().Contain("Private Key Path");
        xaml.Should().Contain("never paste raw private key material");
    }

    [Fact]
    public void IntegrationsView_GitHubAppCard_ShouldExposeConnectionTestActions()
    {
        var xaml = File.ReadAllText(Path.GetFullPath(ViewPath));

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
        var xaml = File.ReadAllText(Path.GetFullPath(ViewPath));
        var view = XDocument.Parse(xaml).Root!;

        xaml.Should().Contain("SETUP_CHECKLIST");
        xaml.Should().Contain("GitHubAppSetupSteps");
        xaml.Should().Contain("HasGitHubAppSetupSteps");

        var githubCard = FindIntegrationCard(view, "GitHub App");
        AttributeValue(githubCard.Elements().Single(e => e.Name.LocalName == "Grid"), "RowDefinitions")
            .Should().Be("Auto,Auto,Auto");

        var checklistHost = githubCard.Descendants()
            .Single(e => e.Name.LocalName == "StackPanel"
                && AttributeValue(e, "IsVisible") == "{Binding HasGitHubAppSetupSteps}");

        checklistHost.Descendants()
            .Single(e => e.Name.LocalName == "TextBlock" && AttributeValue(e, "Text") == "SETUP_CHECKLIST")
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

    private static XElement FindIntegrationCard(XElement root, string title)
    {
        return root.Descendants()
            .Where(e => e.Name.LocalName == "Border" && AttributeValue(e, "Classes") == "IntegrationCard")
            .Single(card => card.Descendants().Any(e =>
                e.Name.LocalName == "TextBlock" && AttributeValue(e, "Text") == title));
    }

    private static string AttributeValue(XElement element, string name)
    {
        return element.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == name)?.Value ?? string.Empty;
    }
}
