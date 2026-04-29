using FluentAssertions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.AI;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.ViewModels.PageModels;

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
}
