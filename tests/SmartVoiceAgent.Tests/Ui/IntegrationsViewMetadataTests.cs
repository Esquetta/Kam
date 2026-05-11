using FluentAssertions;

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
}
