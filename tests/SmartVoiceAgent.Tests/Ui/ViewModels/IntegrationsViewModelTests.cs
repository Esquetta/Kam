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
    public void Constructor_DefaultGitHubState_PresentsDirectConnectionBeforeAdvancedAppSettings()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);

        var viewModel = new IntegrationsViewModel(settingsService);

        viewModel.GitHubAppStatusText.Should().Be("NOT CONNECTED");
        viewModel.GitHubConnectionStatusText.Should().Be("Not connected");
        viewModel.GitHubConnectionDetailText.Should().Contain("Connect GitHub");
        viewModel.ShowGitHubAppAdvancedSettings.Should().BeFalse();
        viewModel.CanConnectGitHub.Should().BeTrue();
    }

    [Fact]
    public async Task ConnectGitHubCommand_WithDesktopConnector_ShowsRepositoryPreview()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var desktopConnector = new RecordingGitHubDesktopConnector
        {
            ConnectResult = GitHubDesktopConnectionResult.Connected(
                "2 repositories visible through your GitHub sign-in.",
                [
                    new GitHubRepositorySummary("Esquetta/Kam", true, "master", "https://github.com/Esquetta/Kam", string.Empty),
                    new GitHubRepositorySummary("Esquetta/KamDocs", false, "main", "https://github.com/Esquetta/KamDocs", string.Empty)
                ])
        };
        var viewModel = new IntegrationsViewModel(settingsService, githubDesktopConnector: desktopConnector);

        await viewModel.ConnectGitHubAsync();

        desktopConnector.ConnectCallCount.Should().Be(1);
        viewModel.GitHubAppStatusText.Should().Be("CONNECTED");
        viewModel.GitHubConnectionStatusText.Should().Be("Connected");
        viewModel.GitHubConnectionDetailText.Should().Contain("2 repositories");
        viewModel.GitHubRepositoryPreviewText.Should().Contain("Esquetta/Kam");
        viewModel.CanListGitHubAppRepositories.Should().BeTrue();
    }

    [Fact]
    public async Task ListGitHubAppRepositoriesCommand_WhenDesktopConnected_RefreshesThroughDesktopConnector()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var desktopConnector = new RecordingGitHubDesktopConnector
        {
            ConnectResult = GitHubDesktopConnectionResult.Connected(
                "1 repository visible through your GitHub sign-in.",
                [
                    new GitHubRepositorySummary("Esquetta/Kam", true, "master", "https://github.com/Esquetta/Kam", string.Empty)
                ]),
            ListResult = GitHubDesktopConnectionResult.Connected(
                "1 repository visible through your GitHub sign-in.",
                [
                    new GitHubRepositorySummary("Esquetta/KamRuntime", true, "main", "https://github.com/Esquetta/KamRuntime", string.Empty)
                ])
        };
        var viewModel = new IntegrationsViewModel(settingsService, githubDesktopConnector: desktopConnector);
        await viewModel.ConnectGitHubAsync();

        await viewModel.ListGitHubAppRepositoriesAsync();

        desktopConnector.ListCallCount.Should().Be(1);
        viewModel.GitHubRepositoryPreviewText.Should().Contain("Esquetta/KamRuntime");
        viewModel.GitHubConnectionStatusText.Should().Be("Connected");
    }

    [Fact]
    public void ToggleGitHubAppAdvancedSettings_ShowsExistingGitHubAppChecklist()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var viewModel = new IntegrationsViewModel(settingsService);

        viewModel.ToggleGitHubAppAdvancedSettingsCommand.Execute(null);

        viewModel.ShowGitHubAppAdvancedSettings.Should().BeTrue();
        viewModel.GitHubConnectionStatusText.Should().Be("Missing settings");
        viewModel.GitHubAppSetupSteps.Should().Contain(step =>
            step.Name == "App ID"
            && step.Value == "Required"
            && step.IsBlocked);
    }

    [Fact]
    public void Constructor_WithoutGitHubAppSettings_ExposesActionableSetupChecklist()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);

        var viewModel = new IntegrationsViewModel(settingsService);

        viewModel.GitHubAppSetupSteps.Should().Contain(step =>
            step.Name == "App ID"
            && step.Value == "Required"
            && step.Detail.Contains("GitHub App settings", StringComparison.OrdinalIgnoreCase)
            && step.IsBlocked);
        viewModel.GitHubAppSetupSteps.Should().Contain(step =>
            step.Name == "Installation ID"
            && step.Value == "Required"
            && step.Detail.Contains("Install the app", StringComparison.OrdinalIgnoreCase)
            && step.IsBlocked);
        viewModel.GitHubAppSetupSteps.Should().Contain(step =>
            step.Name == "Private Key Path"
            && step.Value == "Required"
            && step.Detail.Contains("PEM", StringComparison.OrdinalIgnoreCase)
            && step.IsBlocked);
        viewModel.GitHubAppSetupSteps.Should().Contain(step =>
            step.Name == "Connection Test"
            && step.Value == "Required"
            && step.Detail.Contains("Complete required fields", StringComparison.OrdinalIgnoreCase)
            && step.IsBlocked);
        viewModel.HasGitHubAppSetupSteps.Should().BeTrue();
    }

    [Fact]
    public void GitHubAppSetupChecklist_UpdatesAsFieldsChangeAndWarnsForMissingPemFile()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var missingPemPath = Path.Combine(_settingsDirectory, "missing-key.pem");
        var viewModel = new IntegrationsViewModel(settingsService)
        {
            GitHubAppId = "12345",
            GitHubInstallationId = "98765",
            GitHubPrivateKeyPath = missingPemPath
        };

        viewModel.GitHubAppSetupSteps.Should().Contain(step =>
            step.Name == "App ID"
            && step.Value == "Provided"
            && step.IsReady);
        viewModel.GitHubAppSetupSteps.Should().Contain(step =>
            step.Name == "Installation ID"
            && step.Value == "Provided"
            && step.IsReady);
        viewModel.GitHubAppSetupSteps.Should().Contain(step =>
            step.Name == "Private Key Path"
            && step.Value == "Check path"
            && step.Detail.Contains("PEM file was not found", StringComparison.OrdinalIgnoreCase)
            && step.IsWarning);
        viewModel.GitHubAppSetupSteps.Should().Contain(step =>
            step.Name == "Connection Test"
            && step.Value == "Run test"
            && step.Detail.Contains("Test Connection", StringComparison.OrdinalIgnoreCase)
            && step.IsWarning);
        viewModel.GitHubAppSetupSteps.Select(step => step.Detail).Should().NotContain(missingPemPath);
    }

    [Fact]
    public void GitHubAppSetupChecklist_WhenPemFileExists_MarksPrivateKeyPathReadyWithoutDisplayingPath()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        Directory.CreateDirectory(_settingsDirectory);
        var pemPath = Path.Combine(_settingsDirectory, "kam-github-app.pem");
        File.WriteAllText(pemPath, "not read by the checklist");
        var viewModel = new IntegrationsViewModel(settingsService)
        {
            GitHubAppId = "12345",
            GitHubInstallationId = "98765",
            GitHubPrivateKeyPath = pemPath
        };

        viewModel.GitHubAppSetupSteps.Should().Contain(step =>
            step.Name == "Private Key Path"
            && step.Value == "Provided"
            && step.Detail.Contains("key material is not displayed", StringComparison.OrdinalIgnoreCase)
            && step.IsReady);
        viewModel.GitHubAppSetupSteps.Select(step => step.Detail)
            .Should().NotContain(detail => detail.Contains(pemPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GitHubAppSetupChecklist_WhenPemPathIsMalformed_DoesNotThrowAndWarnsWithoutEchoingInput()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var malformedPath = $"C:{Path.DirectorySeparatorChar}\0not-a-valid-path.pem";
        var viewModel = new IntegrationsViewModel(settingsService)
        {
            GitHubAppId = "12345",
            GitHubInstallationId = "98765"
        };

        var act = () => viewModel.GitHubPrivateKeyPath = malformedPath;

        act.Should().NotThrow();
        viewModel.GitHubAppSetupSteps.Should().Contain(step =>
            step.Name == "Private Key Path"
            && step.Value == "Check path"
            && step.Detail.Contains("PEM file was not found", StringComparison.OrdinalIgnoreCase)
            && step.IsWarning);
        viewModel.GitHubAppSetupSteps.Select(step => step.Detail)
            .Should().NotContain(detail => detail.Contains(malformedPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GitHubAppSetupChecklist_WhenRawPrivateKeyMaterialIsPasted_BlocksAndDoesNotPersistKeyMaterial()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        const string rawPrivateKey = "-----BEGIN PRIVATE KEY-----\nabc123\n-----END PRIVATE KEY-----";
        var viewModel = new IntegrationsViewModel(settingsService)
        {
            GitHubAppId = "12345",
            GitHubInstallationId = "98765",
            GitHubPrivateKeyPath = rawPrivateKey
        };

        viewModel.CanTestGitHubAppConnection.Should().BeFalse();
        viewModel.GitHubAppSetupSteps.Should().Contain(step =>
            step.Name == "Private Key Path"
            && step.Value == "Invalid input"
            && step.Detail.Contains("file path only", StringComparison.OrdinalIgnoreCase)
            && step.IsBlocked);

        viewModel.SaveGitHubAppCommand.Execute(null);

        settingsService.GitHubAppPrivateKeyPath.Should().BeEmpty();
        viewModel.GitHubPrivateKeyPath.Should().BeEmpty();
        viewModel.GitHubConnectionDetailText.Should().NotContain(rawPrivateKey);
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
    public async Task GitHubAppFieldChange_AfterConnectedApp_RequiresRetestBeforeRepositoryListing()
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

        viewModel.GitHubInstallationId = "98766";

        viewModel.CanListGitHubAppRepositories.Should().BeFalse();
        viewModel.GitHubConnectionStatusText.Should().Be("Retest required");
        viewModel.GitHubConnectionDetailText.Should().Contain("Test connection");
        viewModel.GitHubRepositoryPreviewText.Should().BeEmpty();
        viewModel.GitHubAppSetupSteps.Should().Contain(step =>
            step.Name == "Connection Test"
            && step.Value == "Run test"
            && step.IsWarning);
    }

    [Fact]
    public async Task TestGitHubAppConnectionCommand_FailureDetails_RedactCurrentPemPathAndTokens()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var pemPath = Path.Combine(_settingsDirectory, "kam-github-app.pem");
        const string token = "ghp_abcdefghijklmnopqrstuvwxyz";
        var factory = new RecordingGitHubAppClientFactory
        {
            Status = GitHubAppConnectionStatus.Failed(
                $"Failed to load {pemPath} with {token}.",
                "12345",
                "98765",
                "https://api.github.com")
        };
        var viewModel = new IntegrationsViewModel(settingsService, factory)
        {
            GitHubAppId = "12345",
            GitHubInstallationId = "98765",
            GitHubPrivateKeyPath = pemPath
        };

        await viewModel.TestGitHubAppConnectionAsync();

        viewModel.GitHubConnectionDetailText.Should().NotContain(pemPath);
        viewModel.GitHubConnectionDetailText.Should().NotContain(token);
        viewModel.GitHubConnectionDetailText.Should().Contain("[redacted]");
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

    private sealed class RecordingGitHubDesktopConnector : IGitHubDesktopConnector
    {
        public GitHubDesktopConnectionResult ConnectResult { get; set; } =
            GitHubDesktopConnectionResult.Failed("not connected");

        public GitHubDesktopConnectionResult ListResult { get; set; } =
            GitHubDesktopConnectionResult.Failed("not connected");

        public int ConnectCallCount { get; private set; }
        public int ListCallCount { get; private set; }

        public Task<GitHubDesktopConnectionResult> ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectCallCount++;
            return Task.FromResult(ConnectResult);
        }

        public Task<GitHubDesktopConnectionResult> ListRepositoriesAsync(CancellationToken cancellationToken = default)
        {
            ListCallCount++;
            return Task.FromResult(ListResult);
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

        public Task<GitHubPullRequestListResult> ListPullRequestsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GitHubPullRequestListResult.Failed("not used"));
        }

        public Task<GitHubWorkflowRunListResult> ListWorkflowRunsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GitHubWorkflowRunListResult.Failed("not used"));
        }

        public Task<GitHubWorkflowJobListResult> ListWorkflowRunJobsAsync(
            string repositoryFullName,
            long runId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GitHubWorkflowJobListResult.Failed("not used"));
        }

        public Task<GitHubWorkflowJobLogResult> GetWorkflowJobLogAsync(
            string repositoryFullName,
            long jobId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GitHubWorkflowJobLogResult.Failed("not used"));
        }
    }
}
