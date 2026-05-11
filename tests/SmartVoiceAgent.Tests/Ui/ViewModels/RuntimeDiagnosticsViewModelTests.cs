using FluentAssertions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.AI;
using SmartVoiceAgent.Core.Models.GitHub;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Core.Models.Updates;
using SmartVoiceAgent.Infrastructure.Services;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.ViewModels.PageModels;
using System.Reflection;
using System.Security.Cryptography;
using UiCommand = System.Windows.Input.ICommand;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class RuntimeDiagnosticsViewModelTests : IDisposable
{
    private readonly string _settingsDirectory = Path.Combine(
        Path.GetTempPath(),
        "kam-runtime-diagnostics-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Constructor_WithConfiguredPlannerProfile_ReportsCoreAiReadyWithoutLeakingApiKey()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        settingsService.ModelProviderProfiles =
        [
            new ModelProviderProfile
            {
                Id = "planner",
                Provider = ModelProviderType.OpenAI,
                DisplayName = "OpenAI planner",
                Endpoint = "https://api.openai.com/v1",
                ApiKey = "sk-secret-value",
                ModelId = "gpt-5.2",
                Roles = [ModelProviderRole.Planner],
                Enabled = true
            }
        ];
        settingsService.ActivePlannerProfileId = "planner";

        var viewModel = new RuntimeDiagnosticsViewModel(settingsService);

        viewModel.IsCoreReady.Should().BeTrue();
        viewModel.CoreReadinessStatus.Should().Be("READY");
        viewModel.SummaryCards.Should().Contain(card =>
            card.Name == "Core AI" && card.Value == "Ready");
        viewModel.AiRuntimeItems.Should().Contain(item =>
            item.Name == "Planner Model" && item.Value == "OpenAI / gpt-5.2");
        viewModel.AiRuntimeItems.Should().Contain(item =>
            item.Name == "Planner API Key" && item.Value == "Present");
        viewModel.AiRuntimeItems.Select(item => item.Value).Should().NotContain("sk-secret-value");
        viewModel.AiRuntimeItems.Select(item => item.Detail).Should().NotContain("sk-secret-value");
    }

    [Fact]
    public void Constructor_WithoutUsablePlannerProfile_ReportsCoreAiBlocked()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        settingsService.ModelProviderProfiles =
        [
            new ModelProviderProfile
            {
                Id = "planner",
                Provider = ModelProviderType.OpenAI,
                Endpoint = "https://api.openai.com/v1",
                ApiKey = string.Empty,
                ModelId = "gpt-5.2",
                Roles = [ModelProviderRole.Planner],
                Enabled = true
            }
        ];
        settingsService.ActivePlannerProfileId = "planner";

        var viewModel = new RuntimeDiagnosticsViewModel(settingsService);

        viewModel.IsCoreReady.Should().BeFalse();
        viewModel.CoreReadinessStatus.Should().Be("ACTION_NEEDED");
        viewModel.BlockingItems.Should().Contain(item => item.Contains("Planner API key", StringComparison.OrdinalIgnoreCase));
        viewModel.SummaryCards.Should().Contain(card =>
            card.Name == "Core AI" && card.Value == "Action needed");
    }

    [Fact]
    public async Task RefreshAsync_WithOptionalServices_ReportsSkillAndIntegrationReadiness()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        settingsService.ModelProviderProfiles =
        [
            new ModelProviderProfile
            {
                Id = "local-planner",
                Provider = ModelProviderType.Ollama,
                Endpoint = "http://localhost:11434/v1",
                ModelId = "llama3.1",
                Roles = [ModelProviderRole.Planner],
                Enabled = true
            }
        ];
        settingsService.ActivePlannerProfileId = "local-planner";
        settingsService.TodoistApiKey = "todoist-token";
        settingsService.SmtpHost = "smtp.example.com";
        settingsService.SmtpUsername = "user@example.com";
        settingsService.SmtpPassword = "smtp-secret";
        settingsService.SenderEmail = "agent@example.com";
        settingsService.SmsEnabled = true;
        settingsService.TwilioAccountSid = "AC123";
        settingsService.TwilioAuthToken = "twilio-secret";
        settingsService.TwilioPhoneNumber = "+15551234567";

        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            new StaticHostControl(isRunning: true),
            new StaticSkillHealthService(
            [
                new SkillHealthReport { SkillId = "files.read", Status = SkillHealthStatus.Healthy },
                new SkillHealthReport { SkillId = "shell.run", Status = SkillHealthStatus.ReviewRequired }
            ]));

        await viewModel.RefreshAsync();

        viewModel.HostStatus.Should().Be("Online");
        viewModel.SkillStatus.Should().Be("1/2 healthy");
        viewModel.IntegrationItems.Should().Contain(item =>
            item.Name == "Todoist" && item.Value == "Configured");
        viewModel.IntegrationItems.Should().Contain(item =>
            item.Name == "Email SMTP" && item.Value == "Configured");
        viewModel.IntegrationItems.Should().Contain(item =>
            item.Name == "Twilio SMS" && item.Value == "Configured");
        viewModel.SummaryCards.Should().Contain(card =>
            card.Name == "Skills" && card.Value == "1/2 healthy");
    }

    [Fact]
    public async Task RefreshAsync_WithGitHubAppClient_ReportsRepositoryAccessReadiness()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        settingsService.ModelProviderProfiles =
        [
            new ModelProviderProfile
            {
                Id = "local-planner",
                Provider = ModelProviderType.Ollama,
                Endpoint = "http://localhost:11434/v1",
                ModelId = "llama3.1",
                Roles = [ModelProviderRole.Planner],
                Enabled = true
            }
        ];
        settingsService.ActivePlannerProfileId = "local-planner";

        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            githubAppClient: new StaticGitHubAppClient(
                GitHubAppConnectionStatus.Connected(
                    "12345",
                    "98765",
                    "https://api.github.com",
                    "Kam Coding Agent",
                    "kam-coding-agent",
                    2)));

        await viewModel.RefreshAsync();

        viewModel.IntegrationItems.Should().Contain(item =>
            item.Name == "GitHub App"
            && item.Value == "2 repos"
            && item.Detail.Contains("Kam Coding Agent", StringComparison.Ordinal)
            && item.Detail.Contains("repo list access verified", StringComparison.OrdinalIgnoreCase)
            && item.IsReady);
    }

    [Fact]
    public async Task RefreshAsync_WithGitHubAppClient_ListsAccessibleRepositoriesInDiagnostics()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        settingsService.ModelProviderProfiles =
        [
            new ModelProviderProfile
            {
                Id = "local-planner",
                Provider = ModelProviderType.Ollama,
                Endpoint = "http://localhost:11434/v1",
                ModelId = "llama3.1",
                Roles = [ModelProviderRole.Planner],
                Enabled = true
            }
        ];
        settingsService.ActivePlannerProfileId = "local-planner";
        var githubApp = new StaticGitHubAppClient(
            GitHubAppConnectionStatus.Connected(
                "12345",
                "98765",
                "https://api.github.com",
                "Kam Coding Agent",
                "kam-coding-agent",
                4),
            GitHubRepositoryListResult.Succeeded(
                "4 repositories accessible.",
                [
                    new GitHubRepositorySummary(
                        "Esquetta/Kam",
                        true,
                        "master",
                        "https://github.com/Esquetta/Kam",
                        "https://github.com/Esquetta/Kam.git"),
                    new GitHubRepositorySummary(
                        "Esquetta/PublicTool",
                        false,
                        "main",
                        "https://github.com/Esquetta/PublicTool",
                        "https://github.com/Esquetta/PublicTool.git"),
                    new GitHubRepositorySummary(
                        "Esquetta/PrivateOps",
                        true,
                        "main",
                        "https://github.com/Esquetta/PrivateOps",
                        "https://github.com/Esquetta/PrivateOps.git"),
                    new GitHubRepositorySummary(
                        "Zulu/Archive",
                        false,
                        "trunk",
                        "https://github.com/Zulu/Archive",
                        "https://github.com/Zulu/Archive.git")
                ]));

        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            githubAppClient: githubApp);

        await viewModel.RefreshAsync();

        githubApp.ListRepositoriesCallCount.Should().Be(1);
        viewModel.IntegrationItems.Should().Contain(item =>
            item.Name == "GitHub App"
            && item.Value == "4 repos"
            && item.Detail.Contains("Esquetta/Kam (private, master)", StringComparison.Ordinal)
            && item.Detail.Contains("Esquetta/PublicTool (public, main)", StringComparison.Ordinal)
            && item.Detail.Contains("Esquetta/PrivateOps (private, main)", StringComparison.Ordinal)
            && item.Detail.Contains("and 1 more", StringComparison.Ordinal)
            && item.IsReady);
    }

    [Fact]
    public async Task RefreshAsync_WhenGitHubRepositoryListingFails_ReportsWarningWithoutTokenLeak()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        settingsService.ModelProviderProfiles =
        [
            new ModelProviderProfile
            {
                Id = "local-planner",
                Provider = ModelProviderType.Ollama,
                Endpoint = "http://localhost:11434/v1",
                ModelId = "llama3.1",
                Roles = [ModelProviderRole.Planner],
                Enabled = true
            }
        ];
        settingsService.ActivePlannerProfileId = "local-planner";
        const string token = "Bearer ghp_1234567890abcdefghijklmnop";
        var githubApp = new StaticGitHubAppClient(
            GitHubAppConnectionStatus.Connected(
                "12345",
                "98765",
                "https://api.github.com",
                "Kam Coding Agent",
                "kam-coding-agent",
                2),
            GitHubRepositoryListResult.Failed(
                $"GitHub App repository request failed with HTTP 403. {token}",
                []));

        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            githubAppClient: githubApp);

        await viewModel.RefreshAsync();

        var githubItem = viewModel.IntegrationItems.Single(item => item.Name == "GitHub App");
        githubItem.Value.Should().Be("Needs action");
        githubItem.Detail.Should().Contain("repository list");
        githubItem.Detail.Should().Contain("[redacted]");
        githubItem.Detail.Should().NotContain(token);
        githubItem.IsWarning.Should().BeTrue();
    }

    [Fact]
    public async Task RefreshAsync_WithLivePlannerConnectionSuccess_ReportsVerifiedModelConnection()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        settingsService.ModelProviderProfiles =
        [
            new ModelProviderProfile
            {
                Id = "planner",
                Provider = ModelProviderType.OpenAI,
                Endpoint = "https://api.openai.com/v1",
                ApiKey = "sk-secret-value",
                ModelId = "gpt-5.2",
                Roles = [ModelProviderRole.Planner],
                Enabled = true
            }
        ];
        settingsService.ActivePlannerProfileId = "planner";
        var connectionTestService = new RecordingModelConnectionTestService(
            ModelConnectionTestResult.Passed(42));
        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            modelConnectionTestService: connectionTestService);

        await viewModel.RefreshAsync();

        connectionTestService.ProfileIds.Should().ContainSingle().Which.Should().Be("planner");
        viewModel.IsCoreReady.Should().BeTrue();
        viewModel.CoreReadinessStatus.Should().Be("READY");
        viewModel.AiRuntimeItems.Should().Contain(item =>
            item.Name == "Planner Live Connection"
            && item.Value == "Verified"
            && item.Detail.Contains("42 live models", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RefreshAsync_WithLivePlannerConnectionFailure_BlocksCoreReadiness()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        settingsService.ModelProviderProfiles =
        [
            new ModelProviderProfile
            {
                Id = "planner",
                Provider = ModelProviderType.OpenAI,
                Endpoint = "https://api.openai.com/v1",
                ApiKey = "sk-secret-value",
                ModelId = "gpt-5.2",
                Roles = [ModelProviderRole.Planner],
                Enabled = true
            }
        ];
        settingsService.ActivePlannerProfileId = "planner";
        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            modelConnectionTestService: new RecordingModelConnectionTestService(
                ModelConnectionTestResult.Failed("HTTP 401 Unauthorized")));

        await viewModel.RefreshAsync();

        viewModel.IsCoreReady.Should().BeFalse();
        viewModel.CoreReadinessStatus.Should().Be("ACTION_NEEDED");
        viewModel.AiRuntimeItems.Should().Contain(item =>
            item.Name == "Planner Live Connection"
            && item.Value == "Failed"
            && item.Detail.Contains("HTTP 401 Unauthorized", StringComparison.OrdinalIgnoreCase));
        viewModel.BlockingItems.Should().Contain(item =>
            item.Contains("Planner live connection failed", StringComparison.OrdinalIgnoreCase)
            && item.Contains("HTTP 401 Unauthorized", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunSkillSmokeAsync_WithPassingEvalSummary_ReportsRuntimeSkillSmokeReady()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var evalHarness = new RecordingSkillEvalHarness(new SkillEvalSummary
        {
            Total = 2,
            Passed = 2,
            Failed = 0,
            Results =
            [
                new SkillEvalResult
                {
                    Name = "files exists",
                    SkillId = "files.exists",
                    Passed = true,
                    ExpectedStatus = SkillExecutionStatus.Succeeded,
                    ActualStatus = SkillExecutionStatus.Succeeded,
                    Message = "OK",
                    DurationMilliseconds = 12
                }
            ]
        });
        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            skillEvalHarness: evalHarness,
            skillEvalCaseCatalog: new StaticSkillEvalCaseCatalog());

        await viewModel.RunSkillSmokeAsync();

        evalHarness.RunCount.Should().Be(1);
        viewModel.SkillSmokeStatus.Should().Be("2/2 smoke evals passing");
        viewModel.RuntimeItems.Should().Contain(item =>
            item.Name == "Skill Smoke"
            && item.Value == "2/2 passing"
            && item.IsReady);
        viewModel.SummaryCards.Should().Contain(card =>
            card.Name == "Skills" && card.Value == "2/2 smoke");
    }

    [Fact]
    public async Task RunSkillSmokeAsync_WithFailingEvalSummary_ReportsBlockingRuntimeIssue()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            skillEvalHarness: new RecordingSkillEvalHarness(new SkillEvalSummary
            {
                Total = 2,
                Passed = 1,
                Failed = 1,
                Results =
                [
                    new SkillEvalResult
                    {
                        Name = "shell blocked",
                        SkillId = "shell.run",
                        Passed = false,
                        ExpectedStatus = SkillExecutionStatus.Succeeded,
                        ActualStatus = SkillExecutionStatus.PermissionDenied,
                        Message = "Permission denied.",
                        DurationMilliseconds = 4
                    }
                ]
            }),
            skillEvalCaseCatalog: new StaticSkillEvalCaseCatalog());

        await viewModel.RunSkillSmokeAsync();

        viewModel.SkillSmokeStatus.Should().Be("1/2 smoke evals passing");
        viewModel.RuntimeItems.Should().Contain(item =>
            item.Name == "Skill Smoke"
            && item.Value == "1/2 passing"
            && item.IsBlocked);
        viewModel.BlockingItems.Should().Contain(item =>
            item.Contains("Skill smoke failed", StringComparison.OrdinalIgnoreCase)
            && item.Contains("1/2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Constructor_WithRecentPlannerTraceAndSkillExecution_ReportsCommandLoopEvidence()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            skillExecutionHistoryService: new StaticSkillExecutionHistoryService(
            [
                new SkillExecutionHistoryEntry
                {
                    SkillId = "files.read",
                    Status = SkillExecutionStatus.Succeeded,
                    Success = true,
                    ResultSummary = "README loaded.",
                    DurationMilliseconds = 18
                }
            ]),
            skillPlannerTraceStore: new StaticSkillPlannerTraceStore(
            [
                new SkillPlannerTraceEntry
                {
                    IsValid = true,
                    SkillId = "files.read",
                    Confidence = 0.94,
                    DurationMilliseconds = 12
                }
            ]));

        viewModel.RuntimeItems.Should().Contain(item =>
            item.Name == "Planner Trace"
            && item.Value == "Valid"
            && item.IsReady
            && item.Detail.Contains("files.read", StringComparison.OrdinalIgnoreCase));
        viewModel.RuntimeItems.Should().Contain(item =>
            item.Name == "Skill Result"
            && item.Value == "Succeeded"
            && item.IsReady
            && item.Detail.Contains("README loaded", StringComparison.OrdinalIgnoreCase));
        viewModel.SummaryCards.Should().Contain(card =>
            card.Name == "Command Loop"
            && card.Value == "Ready"
            && card.IsReady);
    }

    [Fact]
    public void Constructor_WithInvalidPlannerTraceAndFailedSkillExecution_ReportsCommandLoopBlockers()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            skillExecutionHistoryService: new StaticSkillExecutionHistoryService(
            [
                new SkillExecutionHistoryEntry
                {
                    SkillId = "shell.run",
                    Status = SkillExecutionStatus.PermissionDenied,
                    Success = false,
                    ErrorCode = "shell_command_blocked",
                    ResultSummary = "Command blocked.",
                    DurationMilliseconds = 7
                }
            ]),
            skillPlannerTraceStore: new StaticSkillPlannerTraceStore(
            [
                new SkillPlannerTraceEntry
                {
                    IsValid = false,
                    ErrorMessage = "Planner response must be a single JSON object.",
                    DurationMilliseconds = 9
                }
            ]));

        viewModel.RuntimeItems.Should().Contain(item =>
            item.Name == "Planner Trace"
            && item.Value == "Invalid"
            && item.IsBlocked
            && item.Detail.Contains("single JSON object", StringComparison.OrdinalIgnoreCase));
        viewModel.RuntimeItems.Should().Contain(item =>
            item.Name == "Skill Result"
            && item.Value == "Permission Denied"
            && item.IsBlocked
            && item.Detail.Contains("shell_command_blocked", StringComparison.OrdinalIgnoreCase));
        viewModel.BlockingItems.Should().Contain(item =>
            item.Contains("Planner trace invalid", StringComparison.OrdinalIgnoreCase));
        viewModel.BlockingItems.Should().Contain(item =>
            item.Contains("Last skill execution failed", StringComparison.OrdinalIgnoreCase));
        viewModel.SummaryCards.Should().Contain(card =>
            card.Name == "Command Loop"
            && card.Value == "Action needed"
            && card.IsBlocked);
    }

    [Fact]
    public void Constructor_WithoutCommandLoopEvidence_ReportsCommandLoopNeedsCommand()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            skillExecutionHistoryService: new StaticSkillExecutionHistoryService([]),
            skillPlannerTraceStore: new StaticSkillPlannerTraceStore([]));

        viewModel.SummaryCards.Should().Contain(card =>
            card.Name == "Command Loop"
            && card.Value == "Needs command"
            && card.IsWarning);
    }

    [Fact]
    public async Task LiveTestSession_WithVerifiedCoreSignals_ReportsReadyForLiveProductionTest()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        settingsService.ModelProviderProfiles =
        [
            new ModelProviderProfile
            {
                Id = "planner",
                Provider = ModelProviderType.OpenAI,
                Endpoint = "https://api.openai.com/v1",
                ApiKey = "sk-secret-value",
                ModelId = "gpt-5.2",
                Roles = [ModelProviderRole.Planner],
                Enabled = true
            }
        ];
        settingsService.ActivePlannerProfileId = "planner";
        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            new StaticHostControl(isRunning: true),
            modelConnectionTestService: new RecordingModelConnectionTestService(
                ModelConnectionTestResult.Passed(4)),
            skillEvalHarness: new RecordingSkillEvalHarness(new SkillEvalSummary
            {
                Total = 1,
                Passed = 1,
                Failed = 0,
                Results =
                [
                    new SkillEvalResult
                    {
                        Name = "files exists",
                        SkillId = "files.exists",
                        Passed = true,
                        ExpectedStatus = SkillExecutionStatus.Succeeded,
                        ActualStatus = SkillExecutionStatus.Succeeded,
                        Message = "OK",
                        DurationMilliseconds = 9
                    }
                ]
            }),
            skillEvalCaseCatalog: new StaticSkillEvalCaseCatalog(),
            skillExecutionHistoryService: new StaticSkillExecutionHistoryService(
            [
                new SkillExecutionHistoryEntry
                {
                    SkillId = "files.read",
                    Status = SkillExecutionStatus.Succeeded,
                    Success = true,
                    ResultSummary = "README loaded.",
                    DurationMilliseconds = 18
                }
            ]),
            skillPlannerTraceStore: new StaticSkillPlannerTraceStore(
            [
                new SkillPlannerTraceEntry
                {
                    IsValid = true,
                    SkillId = "files.read",
                    Confidence = 0.93,
                    DurationMilliseconds = 12
                }
            ]));

        await viewModel.RefreshAsync();
        await viewModel.RunSkillSmokeAsync();

        viewModel.IsLiveTestReady.Should().BeTrue();
        viewModel.LiveTestStatus.Should().Be("READY_FOR_LIVE_TEST");
        viewModel.LiveTestNextAction.Should().Be("Run a production command loop smoke.");
        viewModel.LiveTestSteps.Select(step => step.Name).Should().Equal(
            "Core AI",
            "Planner Live Connection",
            "Agent Host",
            "Skill Smoke",
            "Planner Trace",
            "Skill Result",
            "Command Loop");
        viewModel.LiveTestSteps.Should().OnlyContain(step => step.IsReady);
    }

    [Fact]
    public async Task LiveTestSession_WithoutCommandEvidence_ShowsNextActionForCommandLoop()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        settingsService.ModelProviderProfiles =
        [
            new ModelProviderProfile
            {
                Id = "planner",
                Provider = ModelProviderType.OpenAI,
                Endpoint = "https://api.openai.com/v1",
                ApiKey = "sk-secret-value",
                ModelId = "gpt-5.2",
                Roles = [ModelProviderRole.Planner],
                Enabled = true
            }
        ];
        settingsService.ActivePlannerProfileId = "planner";

        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            new StaticHostControl(isRunning: true),
            modelConnectionTestService: new RecordingModelConnectionTestService(
                ModelConnectionTestResult.Passed(4)),
            skillEvalHarness: new RecordingSkillEvalHarness(new SkillEvalSummary
            {
                Total = 1,
                Passed = 1,
                Failed = 0,
                Results =
                [
                    new SkillEvalResult
                    {
                        Name = "files exists",
                        SkillId = "files.exists",
                        Passed = true,
                        ExpectedStatus = SkillExecutionStatus.Succeeded,
                        ActualStatus = SkillExecutionStatus.Succeeded,
                        Message = "OK",
                        DurationMilliseconds = 9
                    }
                ]
            }),
            skillEvalCaseCatalog: new StaticSkillEvalCaseCatalog(),
            skillExecutionHistoryService: new StaticSkillExecutionHistoryService([]),
            skillPlannerTraceStore: new StaticSkillPlannerTraceStore([]));

        await viewModel.RefreshAsync();
        await viewModel.RunSkillSmokeAsync();

        viewModel.IsLiveTestReady.Should().BeFalse();
        viewModel.LiveTestStatus.Should().Be("NEEDS_ACTION");
        viewModel.LiveTestNextAction.Should().Be("Submit a real command to produce planner JSON evidence.");
        viewModel.LiveTestSteps.Should().Contain(step =>
            step.Name == "Planner Trace"
            && step.Value == "No trace"
            && step.IsWarning);
        viewModel.LiveTestSteps.Should().Contain(step =>
            step.Name == "Skill Result"
            && step.Value == "No result"
            && step.IsWarning);
    }

    [Fact]
    public async Task BuildReadinessReport_WithConfiguredRuntime_IncludesReadinessEvidenceWithoutSecrets()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        settingsService.ModelProviderProfiles =
        [
            new ModelProviderProfile
            {
                Id = "planner",
                Provider = ModelProviderType.OpenAI,
                Endpoint = "https://api.openai.com/v1",
                ApiKey = "sk-secret-value",
                ModelId = "gpt-5.2",
                Roles = [ModelProviderRole.Planner],
                Enabled = true
            }
        ];
        settingsService.ActivePlannerProfileId = "planner";
        settingsService.TodoistApiKey = "todoist-secret";
        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            new StaticHostControl(isRunning: true),
            modelConnectionTestService: new RecordingModelConnectionTestService(
                ModelConnectionTestResult.Passed(4)),
            skillEvalHarness: new RecordingSkillEvalHarness(new SkillEvalSummary
            {
                Total = 1,
                Passed = 1,
                Failed = 0,
                Results =
                [
                    new SkillEvalResult
                    {
                        Name = "files exists",
                        SkillId = "files.exists",
                        Passed = true,
                        ExpectedStatus = SkillExecutionStatus.Succeeded,
                        ActualStatus = SkillExecutionStatus.Succeeded,
                        Message = "OK",
                        DurationMilliseconds = 9
                    }
                ]
            }),
            skillEvalCaseCatalog: new StaticSkillEvalCaseCatalog(),
            skillExecutionHistoryService: new StaticSkillExecutionHistoryService(
            [
                new SkillExecutionHistoryEntry
                {
                    SkillId = "files.read",
                    Status = SkillExecutionStatus.Succeeded,
                    Success = true,
                    ResultSummary = "README loaded.",
                    DurationMilliseconds = 18
                }
            ]),
            skillPlannerTraceStore: new StaticSkillPlannerTraceStore(
            [
                new SkillPlannerTraceEntry
                {
                    IsValid = true,
                    SkillId = "files.read",
                    Confidence = 0.93,
                    DurationMilliseconds = 12
                }
            ]));

        await viewModel.RefreshAsync();
        await viewModel.RunSkillSmokeAsync();

        var report = viewModel.BuildReadinessReport();

        report.Should().Contain("Kam Runtime Readiness Report");
        report.Should().Contain("Live Test: READY_FOR_LIVE_TEST");
        report.Should().Contain("Core AI: Ready");
        report.Should().Contain("Planner Live Connection: Verified");
        report.Should().Contain("Skill Smoke: 1/1 passing");
        report.Should().Contain("Command Loop: Ready");
        report.Should().Contain("Planner Model: OpenAI / gpt-5.2");
        report.Should().Contain("Planner API Key: Present");
        report.Should().Contain("Todoist: Configured");
        report.Should().NotContain("sk-secret-value");
        report.Should().NotContain("todoist-secret");
        report.Should().NotContain("https://api.openai.com/v1");
    }

    [Fact]
    public async Task BuildReadinessReport_RedactsSecretsFromPlannerTraceAndSkillHistoryEvidence()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        settingsService.ModelProviderProfiles =
        [
            new ModelProviderProfile
            {
                Id = "planner",
                Provider = ModelProviderType.OpenAI,
                ApiKey = "sk-test-secret",
                ModelId = "gpt-5.2",
                Roles = [ModelProviderRole.Planner],
                Enabled = true
            }
        ];
        settingsService.ActivePlannerProfileId = "planner";
        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            new StaticHostControl(isRunning: true),
            skillExecutionHistoryService: new StaticSkillExecutionHistoryService(
            [
                new SkillExecutionHistoryEntry
                {
                    SkillId = "web.fetch",
                    Status = SkillExecutionStatus.Failed,
                    Success = false,
                    ErrorCode = "http_error",
                    ResultSummary = "Request failed with Bearer abc123 and password=secret.",
                    DurationMilliseconds = 18
                }
            ]),
            skillPlannerTraceStore: new StaticSkillPlannerTraceStore(
            [
                new SkillPlannerTraceEntry
                {
                    IsValid = false,
                    ErrorMessage = "Planner returned api_key=secret and sk-test-secret.",
                    DurationMilliseconds = 12
                }
            ]));

        await viewModel.RefreshAsync();

        var report = viewModel.BuildReadinessReport();

        report.Should().NotContain("sk-test-secret");
        report.Should().NotContain("Bearer abc123");
        report.Should().NotContain("password=secret");
        report.Should().NotContain("api_key=secret");
        report.Should().Contain("[redacted]");
    }

    [Fact]
    public void CopyReadinessReportCommand_InvokesCopyCallbackWithSanitizedReport()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        settingsService.ModelProviderProfiles =
        [
            new ModelProviderProfile
            {
                Id = "planner",
                Provider = ModelProviderType.OpenAI,
                Endpoint = "https://api.openai.com/v1",
                ApiKey = "sk-secret-value",
                ModelId = "gpt-5.2",
                Roles = [ModelProviderRole.Planner],
                Enabled = true
            }
        ];
        settingsService.ActivePlannerProfileId = "planner";
        var copied = new List<(string Label, string Text)>();
        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            copyReport: (label, text) => copied.Add((label, text)));

        viewModel.CopyReadinessReportCommand.Execute(null);

        copied.Should().ContainSingle();
        copied[0].Label.Should().Be("readiness_report");
        copied[0].Text.Should().Contain("Kam Runtime Readiness Report");
        copied[0].Text.Should().NotContain("sk-secret-value");
        viewModel.ReadinessReportCopyStatus.Should().Be("Readiness report copied.");
    }

    [Fact]
    public async Task CheckApplicationUpdateAsync_WithAvailableRelease_ReportsUpdatePanelAction()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var updateService = new RecordingApplicationUpdateService(
            "1.0.0",
            ApplicationUpdateCheckResult.UpdateAvailable(
                "1.0.0",
                "1.2.0",
                "Kam 1.2.0",
                "https://github.com/Esquetta/Kam/releases/tag/v1.2.0",
                new DateTimeOffset(2026, 5, 9, 10, 30, 0, TimeSpan.Zero),
                new ApplicationUpdateAsset(
                    "Kam-1.2.0-x64.msi",
                    "https://downloads.example/Kam-1.2.0-x64.msi",
                    1_048_576,
                    "application/octet-stream")));

        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            applicationUpdateService: updateService,
            applicationVersionProvider: new StaticApplicationVersionProvider("1.0.0"));

        await viewModel.CheckApplicationUpdateAsync();

        updateService.CheckCount.Should().Be(1);
        viewModel.ApplicationUpdateStatus.Should().Be("Update available");
        viewModel.ApplicationUpdateActionStatus.Should().Be("Kam 1.2.0 is available.");
        viewModel.ApplicationUpdateItems.Should().Contain(item =>
            item.Name == "Release Feed"
            && item.Value == "Update available"
            && item.IsWarning);
        viewModel.ApplicationUpdateItems.Should().Contain(item =>
            item.Name == "Release Asset"
            && item.Value == "Kam-1.2.0-x64.msi"
            && item.Detail.Contains("1 MB", StringComparison.OrdinalIgnoreCase));
        viewModel.SummaryCards.Should().Contain(card =>
            card.Name == "Updates"
            && card.Value == "Update available"
            && card.IsWarning);
    }

    [Fact]
    public async Task DownloadApplicationUpdateAsync_WithVerifiedDownloadedInstaller_BuildsRestartPlan()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        const string packagePath = @"C:\Users\agent\AppData\Local\Kam\Updates\Kam-1.2.0-x64.msi";
        var updateService = new RecordingApplicationUpdateService(
            "1.0.0",
            ApplicationUpdateCheckResult.UpdateAvailable(
                "1.0.0",
                "1.2.0",
                "Kam 1.2.0",
                "https://github.com/Esquetta/Kam/releases/tag/v1.2.0",
                null,
                new ApplicationUpdateAsset(
                    "Kam-1.2.0-x64.msi",
                    "https://downloads.example/Kam-1.2.0-x64.msi",
                    2_097_152,
                    "application/octet-stream")),
            ApplicationUpdateDownloadResult.Succeeded(
                packagePath,
                "1.2.0",
                2_097_152,
                isVerified: true,
                verificationStatus: "SHA256 verified",
                expectedSha256: new string('a', 64),
                actualSha256: new string('a', 64)));
        var restartPlanner = new RecordingApplicationRestartPlanner(
            new ApplicationRestartPlan(
                true,
                "Installer handoff ready.",
                @"C:\Program Files\Kam\Kam.exe",
                packagePath,
                ["Start installer", "Close Kam", "Relaunch Kam"]));

        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            applicationUpdateService: updateService,
            applicationRestartPlanner: restartPlanner,
            applicationVersionProvider: new StaticApplicationVersionProvider("1.0.0"));

        await viewModel.DownloadApplicationUpdateAsync();

        updateService.DownloadCount.Should().Be(1);
        restartPlanner.LastPackagePath.Should().Be(packagePath);
        viewModel.DownloadedUpdatePackagePath.Should().Be(packagePath);
        viewModel.ApplicationUpdateActionStatus.Should().Be("Downloaded Kam update package.");
        viewModel.ApplicationUpdateItems.Should().Contain(item =>
            item.Name == "Downloaded Package"
            && item.Value == "Ready"
            && item.Detail.Contains("Kam-1.2.0-x64.msi", StringComparison.OrdinalIgnoreCase));
        viewModel.ApplicationUpdateItems.Should().Contain(item =>
            item.Name == "Package Verification"
            && item.Value == "Verified"
            && item.IsReady);
        viewModel.ApplicationUpdateItems.Should().Contain(item =>
            item.Name == "Restart Plan"
            && item.Value == "Ready"
            && item.Detail.Contains("Installer handoff ready", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanApplicationRestart_WhenVerifiedPackageChangesAfterDownload_BlocksPlannerHandoff()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var packagePath = CreatePackage("Kam-1.2.0-x64.msi", "verified-package");
        var sha256 = ComputeSha256(packagePath);
        var updateService = new RecordingApplicationUpdateService(
            "1.0.0",
            ApplicationUpdateCheckResult.UpdateAvailable(
                "1.0.0",
                "1.2.0",
                "Kam 1.2.0",
                "https://github.com/Esquetta/Kam/releases/tag/v1.2.0",
                null,
                new ApplicationUpdateAsset(
                    "Kam-1.2.0-x64.msi",
                    "https://downloads.example/Kam-1.2.0-x64.msi",
                    2_097_152,
                    "application/octet-stream")),
            ApplicationUpdateDownloadResult.Succeeded(
                packagePath,
                "1.2.0",
                new FileInfo(packagePath).Length,
                isVerified: true,
                verificationStatus: "SHA256 verified",
                expectedSha256: sha256,
                actualSha256: sha256));
        var restartPlanner = new RecordingApplicationRestartPlanner(
            new ApplicationRestartPlan(
                true,
                "Installer handoff ready.",
                @"C:\Program Files\Kam\Kam.exe",
                packagePath,
                ["Start installer", "Close Kam", "Relaunch Kam"]));
        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            applicationUpdateService: updateService,
            applicationRestartPlanner: restartPlanner,
            applicationVersionProvider: new StaticApplicationVersionProvider("1.0.0"),
            applicationUpdateSession: new ApplicationUpdateSession());

        await viewModel.DownloadApplicationUpdateAsync();
        File.AppendAllText(packagePath, "-tampered");
        viewModel.PlanApplicationRestart();

        restartPlanner.CreateCount.Should().Be(1);
        viewModel.ApplicationUpdateActionStatus.Should().Contain("SHA256 no longer matches");
        viewModel.ApplicationUpdateItems.Should().Contain(item =>
            item.Name == "Restart Plan"
            && item.Value == "Blocked"
            && item.Detail.Contains("SHA256 no longer matches", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlanApplicationRestart_WhenSharedSessionHasVerifiedPackage_UsesSessionPackage()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var packagePath = CreatePackage("Kam-1.2.0-x64.msi", "verified-package");
        var sha256 = ComputeSha256(packagePath);
        var updateSession = new ApplicationUpdateSession();
        updateSession.RecordDownload(ApplicationUpdateDownloadResult.Succeeded(
            packagePath,
            "1.2.0",
            new FileInfo(packagePath).Length,
            isVerified: true,
            verificationStatus: "SHA256 verified",
            expectedSha256: sha256,
            actualSha256: sha256));
        var restartPlanner = new RecordingApplicationRestartPlanner(
            new ApplicationRestartPlan(
                true,
                "Installer handoff ready.",
                @"C:\Program Files\Kam\Kam.exe",
                packagePath,
                ["Start installer", "Close Kam", "Relaunch Kam"]));
        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            applicationRestartPlanner: restartPlanner,
            applicationVersionProvider: new StaticApplicationVersionProvider("1.0.0"),
            applicationUpdateSession: updateSession);

        viewModel.PlanApplicationRestart();

        restartPlanner.LastPackagePath.Should().Be(packagePath);
        viewModel.DownloadedUpdatePackagePath.Should().Be(packagePath);
        viewModel.ApplicationUpdateItems.Should().Contain(item =>
            item.Name == "Restart Plan"
            && item.Value == "Ready"
            && item.Detail.Contains("Installer handoff ready", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DownloadApplicationUpdateAsync_WithUnverifiedDownloadedInstaller_DoesNotBuildRestartPlan()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        const string packagePath = @"C:\Users\agent\AppData\Local\Kam\Updates\Kam-1.2.0-x64.msi";
        var updateService = new RecordingApplicationUpdateService(
            "1.0.0",
            ApplicationUpdateCheckResult.UpdateAvailable(
                "1.0.0",
                "1.2.0",
                "Kam 1.2.0",
                "https://github.com/Esquetta/Kam/releases/tag/v1.2.0",
                null,
                new ApplicationUpdateAsset(
                    "Kam-1.2.0-x64.msi",
                    "https://downloads.example/Kam-1.2.0-x64.msi",
                    2_097_152,
                    "application/octet-stream")),
            ApplicationUpdateDownloadResult.Succeeded(
                packagePath,
                "1.2.0",
                2_097_152,
                isVerified: false,
                verificationStatus: "Checksum missing"));
        var restartPlanner = new RecordingApplicationRestartPlanner(
            new ApplicationRestartPlan(
                true,
                "Installer handoff ready.",
                @"C:\Program Files\Kam\Kam.exe",
                packagePath,
                ["Start installer", "Close Kam", "Relaunch Kam"]));

        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            applicationUpdateService: updateService,
            applicationRestartPlanner: restartPlanner,
            applicationVersionProvider: new StaticApplicationVersionProvider("1.0.0"));

        await viewModel.DownloadApplicationUpdateAsync();

        restartPlanner.LastPackagePath.Should().BeNull();
        viewModel.ApplicationUpdateActionStatus.Should().Be("Verify the downloaded package before restart handoff.");
        viewModel.ApplicationUpdateItems.Should().Contain(item =>
            item.Name == "Package Verification"
            && item.Value == "Not verified"
            && item.IsWarning);
        viewModel.ApplicationUpdateItems.Should().Contain(item =>
            item.Name == "Restart Plan"
            && item.Value == "Blocked"
            && item.Detail.Contains("Verify the downloaded package", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildReadinessReport_WithApplicationUpdateEvidence_IncludesUpdateSectionWithoutAssetUrl()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var updateService = new RecordingApplicationUpdateService(
            "1.2.0",
            ApplicationUpdateCheckResult.UpToDate(
                "1.2.0",
                "1.2.0",
                "Kam 1.2.0",
                "https://github.com/Esquetta/Kam/releases/tag/v1.2.0",
                new DateTimeOffset(2026, 5, 9, 10, 30, 0, TimeSpan.Zero)));
        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            applicationUpdateService: updateService,
            applicationVersionProvider: new StaticApplicationVersionProvider("1.2.0"));

        await viewModel.CheckApplicationUpdateAsync();

        var report = viewModel.BuildReadinessReport();

        report.Should().Contain("Application Updates");
        report.Should().Contain("Current Version: 1.2.0");
        report.Should().Contain("Release Feed: Up to date");
        report.Should().NotContain("https://downloads.example");
    }

    [Fact]
    public void RuntimeDiagnosticsViewModel_ShouldExposeApplicationUpdateStatusAndVersionSurface()
    {
        var diagnosticsType = typeof(RuntimeDiagnosticsViewModel);
        var updateStatusProperties = diagnosticsType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property =>
                property.PropertyType == typeof(string) &&
                property.Name.Contains("Update", StringComparison.OrdinalIgnoreCase) &&
                property.Name.Contains("Status", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        updateStatusProperties.Should().NotBeEmpty(
            "runtime diagnostics should expose an application update status string for the UX slice");

        var versionProperties = diagnosticsType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property =>
                property.PropertyType == typeof(string) &&
                property.Name.Contains("Version", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        versionProperties.Should().NotBeEmpty(
            "runtime diagnostics should expose version context for update readiness reporting");
    }

    [Fact]
    public void RuntimeDiagnosticsViewModel_ShouldExposeUpdateLifecycleCommands()
    {
        var commandProperties = typeof(RuntimeDiagnosticsViewModel).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => typeof(UiCommand).IsAssignableFrom(property.PropertyType))
            .Select(property => property.Name)
            .ToArray();

        commandProperties.Should().Contain(name =>
                name.Contains("Check", StringComparison.OrdinalIgnoreCase) &&
                name.Contains("Update", StringComparison.OrdinalIgnoreCase),
            "runtime diagnostics should expose a command to run an update check");

        commandProperties.Should().Contain(name =>
                name.Contains("Download", StringComparison.OrdinalIgnoreCase) &&
                name.Contains("Update", StringComparison.OrdinalIgnoreCase),
            "runtime diagnostics should expose a command to download an update package");

        commandProperties.Should().Contain(name =>
                name.Contains("Restart", StringComparison.OrdinalIgnoreCase) &&
                (name.Contains("Plan", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Apply", StringComparison.OrdinalIgnoreCase)),
            "runtime diagnostics should expose a command for creating a restart plan");
    }

    [Fact]
    public async Task BuildReadinessReport_ShouldNotLeakSensitiveValuesInAnyUpdateEvidence()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        settingsService.ModelProviderProfiles =
        [
            new ModelProviderProfile
            {
                Id = "planner",
                Provider = ModelProviderType.OpenAI,
                ApiKey = "sk-super-secret",
                ModelId = "gpt-5.2",
                Roles = [ModelProviderRole.Planner],
                Enabled = true
            }
        ];
        settingsService.ActivePlannerProfileId = "planner";
        settingsService.SmtpPassword = "smtp-super-secret";
        settingsService.TodoistApiKey = "todoist-super-secret";

        var viewModel = new RuntimeDiagnosticsViewModel(
            settingsService,
            new StaticHostControl(isRunning: true));

        await viewModel.RefreshAsync();

        var report = viewModel.BuildReadinessReport();

        report.Should().NotContain("sk-super-secret");
        report.Should().NotContain("smtp-super-secret");
        report.Should().NotContain("todoist-super-secret");
    }

    public void Dispose()
    {
        if (Directory.Exists(_settingsDirectory))
        {
            Directory.Delete(_settingsDirectory, recursive: true);
        }
    }

    private sealed class StaticHostControl : IVoiceAgentHostControl
    {
        public StaticHostControl(bool isRunning)
        {
            IsRunning = isRunning;
        }

        public bool IsRunning { get; }

        public event EventHandler<bool>? StateChanged;

        public Task StartAsync()
        {
            StateChanged?.Invoke(this, true);
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StateChanged?.Invoke(this, false);
            return Task.CompletedTask;
        }
    }

    private sealed class StaticSkillHealthService : ISkillHealthService
    {
        private readonly IReadOnlyCollection<SkillHealthReport> _reports;

        public StaticSkillHealthService(IReadOnlyCollection<SkillHealthReport> reports)
        {
            _reports = reports;
        }

        public Task<IReadOnlyCollection<SkillHealthReport>> GetHealthAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_reports);
        }
    }

    private sealed class RecordingModelConnectionTestService : IModelConnectionTestService
    {
        private readonly ModelConnectionTestResult _result;

        public RecordingModelConnectionTestService(ModelConnectionTestResult result)
        {
            _result = result;
        }

        public List<string> ProfileIds { get; } = [];

        public Task<ModelConnectionTestResult> TestAsync(
            ModelProviderProfile profile,
            CancellationToken cancellationToken = default)
        {
            ProfileIds.Add(profile.Id);
            return Task.FromResult(_result);
        }
    }

    private sealed class StaticGitHubAppClient : IGitHubAppClient
    {
        private readonly GitHubAppConnectionStatus _status;
        private readonly GitHubRepositoryListResult _repositories;

        public StaticGitHubAppClient(GitHubAppConnectionStatus status)
            : this(
                status,
                GitHubRepositoryListResult.Succeeded(
                    "0 repositories accessible.",
                    []))
        {
        }

        public StaticGitHubAppClient(
            GitHubAppConnectionStatus status,
            GitHubRepositoryListResult repositories)
        {
            _status = status;
            _repositories = repositories;
        }

        public int ListRepositoriesCallCount { get; private set; }

        public Task<GitHubAppConnectionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_status);
        }

        public Task<GitHubRepositoryListResult> ListRepositoriesAsync(CancellationToken cancellationToken = default)
        {
            ListRepositoriesCallCount++;
            return Task.FromResult(_repositories);
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
    }

    private sealed class RecordingSkillEvalHarness : ISkillEvalHarness
    {
        private readonly SkillEvalSummary _summary;

        public RecordingSkillEvalHarness(SkillEvalSummary summary)
        {
            _summary = summary;
        }

        public int RunCount { get; private set; }

        public Task<SkillEvalSummary> RunAsync(
            IEnumerable<SkillEvalCase> cases,
            CancellationToken cancellationToken = default)
        {
            RunCount++;
            cases.Should().NotBeEmpty();
            return Task.FromResult(_summary);
        }
    }

    private sealed class StaticSkillEvalCaseCatalog : ISkillEvalCaseCatalog
    {
        public IReadOnlyCollection<SkillEvalCase> CreateSmokeCases()
        {
            return
            [
                new SkillEvalCase
                {
                    Name = "files exists",
                    Plan = SkillPlan.FromObject("files.exists", new { path = "README.md" })
                }
            ];
        }
    }

    private sealed class StaticSkillExecutionHistoryService : ISkillExecutionHistoryService
    {
        private readonly IReadOnlyList<SkillExecutionHistoryEntry> _entries;

        public StaticSkillExecutionHistoryService(IReadOnlyList<SkillExecutionHistoryEntry> entries)
        {
            _entries = entries;
        }

        public event EventHandler? Changed;

        public IReadOnlyList<SkillExecutionHistoryEntry> GetRecent(int maxCount = 50)
        {
            return _entries.Take(maxCount).ToArray();
        }

        public SkillExecutionHistoryEntry Record(
            SkillPlan plan,
            SkillResult result,
            DateTimeOffset? timestamp = null)
        {
            var entry = new SkillExecutionHistoryEntry();
            Changed?.Invoke(this, EventArgs.Empty);
            return entry;
        }

        public void Clear()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class StaticSkillPlannerTraceStore : ISkillPlannerTraceStore
    {
        private readonly IReadOnlyList<SkillPlannerTraceEntry> _entries;

        public StaticSkillPlannerTraceStore(IReadOnlyList<SkillPlannerTraceEntry> entries)
        {
            _entries = entries;
        }

        public event EventHandler? Changed;

        public IReadOnlyList<SkillPlannerTraceEntry> GetRecent(int maxCount = 20)
        {
            return _entries.Take(maxCount).ToArray();
        }

        public SkillPlannerTraceEntry Record(SkillPlannerTraceEntry entry)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return entry;
        }

        public void Clear()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class StaticApplicationVersionProvider : IApplicationVersionProvider
    {
        public StaticApplicationVersionProvider(string currentVersion)
        {
            CurrentVersion = currentVersion;
        }

        public string CurrentVersion { get; }
    }

    private sealed class RecordingApplicationUpdateService : IApplicationUpdateService
    {
        private readonly ApplicationUpdateCheckResult _checkResult;
        private readonly ApplicationUpdateDownloadResult _downloadResult;

        public RecordingApplicationUpdateService(
            string currentVersion,
            ApplicationUpdateCheckResult checkResult,
            ApplicationUpdateDownloadResult? downloadResult = null)
        {
            CurrentVersion = currentVersion;
            _checkResult = checkResult;
            _downloadResult = downloadResult ?? ApplicationUpdateDownloadResult.Failed("No download configured.");
        }

        public string CurrentVersion { get; }

        public int CheckCount { get; private set; }

        public int DownloadCount { get; private set; }

        public Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync(
            CancellationToken cancellationToken = default)
        {
            CheckCount++;
            return Task.FromResult(_checkResult);
        }

        public Task<ApplicationUpdateDownloadResult> DownloadLatestAsync(
            CancellationToken cancellationToken = default)
        {
            DownloadCount++;
            return Task.FromResult(_downloadResult);
        }
    }

    private sealed class RecordingApplicationRestartPlanner : IApplicationRestartPlanner
    {
        private readonly ApplicationRestartPlan _plan;

        public RecordingApplicationRestartPlanner(ApplicationRestartPlan plan)
        {
            _plan = plan;
        }

        public string? LastPackagePath { get; private set; }

        public int CreateCount { get; private set; }

        public ApplicationRestartPlan CreateRestartPlan(string? updatePackagePath = null)
        {
            CreateCount++;
            LastPackagePath = updatePackagePath;
            return _plan;
        }
    }

    private string CreatePackage(string fileName, string contents)
    {
        Directory.CreateDirectory(_settingsDirectory);
        var packagePath = Path.Combine(_settingsDirectory, fileName);
        File.WriteAllText(packagePath, contents);
        return packagePath;
    }

    private static string ComputeSha256(string filePath)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(filePath))).ToLowerInvariant();
    }
}
