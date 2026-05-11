using FluentAssertions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.GitHub;
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

    [Fact]
    public async Task TestGitHubAppConnectionCommand_ConnectedApp_ShowsRepositoryPreview()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var factory = new RecordingGitHubAppClientFactory
        {
            Status = GitHubAppConnectionStatus.Connected(
                "12345",
                "98765",
                "https://api.github.com",
                "Kam",
                "kam",
                2),
            Repositories = GitHubRepositoryListResult.Succeeded(
                "2 repositories visible.",
                [
                    new GitHubRepositorySummary("Esquetta/Kam", true, "master", "https://github.com/Esquetta/Kam", "https://github.com/Esquetta/Kam.git"),
                    new GitHubRepositorySummary("Esquetta/KamDocs", false, "main", "https://github.com/Esquetta/KamDocs", "https://github.com/Esquetta/KamDocs.git")
                ])
        };
        var viewModel = new IntegrationsViewModel(settingsService, factory)
        {
            GitHubAppId = "12345",
            GitHubInstallationId = "98765",
            GitHubPrivateKeyPath = @"C:\secure\kam-github-app.pem"
        };

        await viewModel.TestGitHubAppConnectionAsync();

        factory.CreatedOptions.Should().NotBeNull();
        factory.CreatedOptions!.AppId.Should().Be("12345");
        factory.CreatedOptions.InstallationId.Should().Be("98765");
        factory.CreatedOptions.PrivateKeyPath.Should().Be(@"C:\secure\kam-github-app.pem");
        factory.Client.GetStatusCallCount.Should().Be(1);
        factory.Client.ListRepositoriesCallCount.Should().Be(1);
        viewModel.IsTestingGitHubAppConnection.Should().BeFalse();
        viewModel.GitHubConnectionStatusText.Should().Be("Connected");
        viewModel.GitHubConnectionDetailText.Should().Contain("2 repositories");
        viewModel.GitHubRepositoryPreviewText.Should().Contain("Esquetta/Kam");
        viewModel.HasGitHubRepositoryPreview.Should().BeTrue();
        viewModel.CanListGitHubAppRepositories.Should().BeTrue();
    }

    [Fact]
    public async Task TestGitHubAppConnectionCommand_MissingConfig_ShowsValidationWithoutCallingClient()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var factory = new RecordingGitHubAppClientFactory();
        var viewModel = new IntegrationsViewModel(settingsService, factory)
        {
            GitHubAppId = "12345",
            GitHubInstallationId = string.Empty,
            GitHubPrivateKeyPath = @"C:\secure\kam-github-app.pem"
        };

        await viewModel.TestGitHubAppConnectionAsync();

        factory.Client.GetStatusCallCount.Should().Be(0);
        factory.Client.ListRepositoriesCallCount.Should().Be(0);
        viewModel.GitHubConnectionStatusText.Should().Be("Missing settings");
        viewModel.GitHubConnectionDetailText.Should().Contain("Installation ID");
        viewModel.CanListGitHubAppRepositories.Should().BeFalse();
    }

    [Fact]
    public async Task ListGitHubAppRepositoriesCommand_WhenConnected_ShowsRepositoryList()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var factory = new RecordingGitHubAppClientFactory
        {
            Status = GitHubAppConnectionStatus.Connected(
                "12345",
                "98765",
                "https://api.github.com",
                "Kam",
                "kam",
                1),
            Repositories = GitHubRepositoryListResult.Succeeded(
                "1 repository visible.",
                [
                    new GitHubRepositorySummary("Esquetta/Kam", true, "master", "https://github.com/Esquetta/Kam", "https://github.com/Esquetta/Kam.git")
                ])
        };
        var viewModel = new IntegrationsViewModel(settingsService, factory)
        {
            GitHubAppId = "12345",
            GitHubInstallationId = "98765",
            GitHubPrivateKeyPath = @"C:\secure\kam-github-app.pem"
        };
        await viewModel.TestGitHubAppConnectionAsync();
        factory.Client.ListRepositoriesCallCount = 0;

        await viewModel.ListGitHubAppRepositoriesAsync();

        factory.Client.ListRepositoriesCallCount.Should().Be(1);
        viewModel.GitHubRepositoryPreviewText.Should().Contain("Esquetta/Kam");
        viewModel.GitHubRepositoryPreviewText.Should().Contain("master");
        viewModel.GitHubConnectionStatusText.Should().Be("Connected");
    }

    [Fact]
    public async Task ListGitHubAppRepositoriesCommand_WhenNotConnected_BlocksAndWarns()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var factory = new RecordingGitHubAppClientFactory();
        var viewModel = new IntegrationsViewModel(settingsService, factory)
        {
            GitHubAppId = "12345",
            GitHubInstallationId = "98765",
            GitHubPrivateKeyPath = @"C:\secure\kam-github-app.pem"
        };

        await viewModel.ListGitHubAppRepositoriesAsync();

        factory.Client.ListRepositoriesCallCount.Should().Be(0);
        viewModel.GitHubConnectionStatusText.Should().Be("Not tested");
        viewModel.GitHubConnectionDetailText.Should().Contain("Test connection");
        viewModel.GitHubRepositoryPreviewText.Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_settingsDirectory))
        {
            Directory.Delete(_settingsDirectory, recursive: true);
        }
    }

    private sealed class RecordingGitHubAppClientFactory : IGitHubAppClientFactory
    {
        public RecordingGitHubAppClient Client { get; } = new();

        public GitHubAppOptions? CreatedOptions { get; private set; }

        public GitHubAppConnectionStatus Status
        {
            get => Client.Status;
            set => Client.Status = value;
        }

        public GitHubRepositoryListResult Repositories
        {
            get => Client.Repositories;
            set => Client.Repositories = value;
        }

        public IGitHubAppClient Create(GitHubAppOptions options)
        {
            CreatedOptions = options;
            return Client;
        }
    }

    private sealed class RecordingGitHubAppClient : IGitHubAppClient
    {
        public GitHubAppConnectionStatus Status { get; set; } = GitHubAppConnectionStatus.NotConfigured(
            "GitHub App is not configured.",
            [GitHubAppOptions.SectionName + ":AppId"]);

        public GitHubRepositoryListResult Repositories { get; set; } = GitHubRepositoryListResult.Failed(
            "Repository list is unavailable.");

        public int GetStatusCallCount { get; private set; }

        public int ListRepositoriesCallCount { get; set; }

        public Task<GitHubAppConnectionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            GetStatusCallCount++;
            return Task.FromResult(Status);
        }

        public Task<GitHubRepositoryListResult> ListRepositoriesAsync(CancellationToken cancellationToken = default)
        {
            ListRepositoriesCallCount++;
            return Task.FromResult(Repositories);
        }
    }
}
