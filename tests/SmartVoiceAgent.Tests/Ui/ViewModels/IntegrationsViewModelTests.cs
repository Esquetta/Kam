using FluentAssertions;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.ViewModels.PageModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class IntegrationsViewModelTests : IDisposable
{
    private readonly string _settingsDirectory = Path.Combine(
        Path.GetTempPath(),
        "kam-integrations-viewmodel-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Constructor_LoadsGitHubAppSettingsFromSettingsService()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        settingsService.GitHubAppId = "12345";
        settingsService.GitHubAppInstallationId = "98765";
        settingsService.GitHubAppPrivateKeyPath = @"C:\secure\kam-github-app.pem";

        var viewModel = new IntegrationsViewModel(settingsService);

        viewModel.GitHubAppId.Should().Be("12345");
        viewModel.GitHubInstallationId.Should().Be("98765");
        viewModel.GitHubPrivateKeyPath.Should().Be(@"C:\secure\kam-github-app.pem");
        viewModel.IsGitHubAppConfigured.Should().BeTrue();
        viewModel.GitHubAppStatusText.Should().Be("CONFIGURED");
    }

    [Fact]
    public void SaveGitHubAppCommand_PersistsGitHubAppSettings()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var viewModel = new IntegrationsViewModel(settingsService)
        {
            GitHubAppId = "12345",
            GitHubInstallationId = "98765",
            GitHubPrivateKeyPath = @"C:\secure\kam-github-app.pem"
        };

        viewModel.SaveGitHubAppCommand.Execute(null);

        settingsService.GitHubAppId.Should().Be("12345");
        settingsService.GitHubAppInstallationId.Should().Be("98765");
        settingsService.GitHubAppPrivateKeyPath.Should().Be(@"C:\secure\kam-github-app.pem");
        viewModel.IsGitHubAppConfigured.Should().BeTrue();
    }

    [Fact]
    public void ClearGitHubAppCommand_RemovesPersistedGitHubAppSettings()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        settingsService.GitHubAppId = "12345";
        settingsService.GitHubAppInstallationId = "98765";
        settingsService.GitHubAppPrivateKeyPath = @"C:\secure\kam-github-app.pem";
        var viewModel = new IntegrationsViewModel(settingsService);

        viewModel.ClearGitHubAppCommand.Execute(null);

        viewModel.GitHubAppId.Should().BeEmpty();
        viewModel.GitHubInstallationId.Should().BeEmpty();
        viewModel.GitHubPrivateKeyPath.Should().BeEmpty();
        settingsService.GitHubAppId.Should().BeEmpty();
        settingsService.GitHubAppInstallationId.Should().BeEmpty();
        settingsService.GitHubAppPrivateKeyPath.Should().BeEmpty();
        viewModel.IsGitHubAppConfigured.Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_settingsDirectory))
        {
            Directory.Delete(_settingsDirectory, recursive: true);
        }
    }
}
