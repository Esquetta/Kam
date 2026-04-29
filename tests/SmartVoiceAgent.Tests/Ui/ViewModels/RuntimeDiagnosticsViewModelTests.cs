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
        viewModel.LiveTestNextAction.Should().Be("Start a local production session.");
        viewModel.LiveTestSteps.Should().HaveCount(5);
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
        viewModel.LiveTestNextAction.Should().Be("Submit a real command to verify planner and skill execution.");
        viewModel.LiveTestSteps.Should().Contain(step =>
            step.Name == "Command Loop"
            && step.Value == "Needs command"
            && step.IsWarning);
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
}
