using FluentAssertions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Adapters;
using SmartVoiceAgent.Infrastructure.Skills.Importing;
using SmartVoiceAgent.Infrastructure.Skills.Policy;
using SmartVoiceAgent.Ui.ViewModels.PageModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public class PluginsViewModelSkillHealthTests
{
    [Fact]
    public void Constructor_WithSkillHealthReports_MapsReportsToPluginCards()
    {
        var viewModel = new PluginsViewModel(
        [
            new SkillHealthReport
            {
                SkillId = "files.read",
                DisplayName = "Read File",
                Description = "Reads a local file.",
                Source = "builtin",
                Status = SkillHealthStatus.Healthy,
                Details = "Executor available."
            },
            new SkillHealthReport
            {
                SkillId = "mcp.todoist.add_task",
                DisplayName = "Add Todoist Task",
                Description = "Creates a Todoist task.",
                Source = "mcp.todoist",
                Status = SkillHealthStatus.MissingExecutor,
                Details = "No executor registered for this skill."
            },
            new SkillHealthReport
            {
                SkillId = "local.desktop-navigation",
                DisplayName = "Desktop Navigation",
                Description = "Imported local skill.",
                Source = "local:C:\\skills\\desktop-navigation",
                Status = SkillHealthStatus.ReviewRequired,
                Details = "Skill requires review before it can be enabled.",
                RequiredPermissions = [SkillPermission.ProcessControl],
                GrantedPermissions = []
            }
        ]);

        viewModel.Plugins.Should().HaveCount(3);

        var healthy = viewModel.Plugins[0];
        healthy.Name.Should().Be("READ FILE");
        healthy.SkillId.Should().Be("files.read");
        healthy.Source.Should().Be("builtin");
        healthy.Status.Should().Be("Healthy");
        healthy.HealthDetail.Should().Be("Executor available.");
        healthy.IsActive.Should().BeTrue();

        var missing = viewModel.Plugins[1];
        missing.Name.Should().Be("ADD TODOIST TASK");
        missing.Status.Should().Be("Missing Executor");
        missing.HealthDetail.Should().Contain("No executor");
        missing.IsActive.Should().BeFalse();

        var reviewRequired = viewModel.Plugins[2];
        reviewRequired.Name.Should().Be("DESKTOP NAVIGATION");
        reviewRequired.Status.Should().Be("Review Required");
        reviewRequired.HealthDetail.Should().Contain("requires review");
        reviewRequired.IsActive.Should().BeFalse();
        reviewRequired.CanApproveReview.Should().BeTrue();
        reviewRequired.CanDisable.Should().BeFalse();
        reviewRequired.CanEnable.Should().BeFalse();
        reviewRequired.CanRevokePermissions.Should().BeFalse();
        reviewRequired.RequiredPermissionsText.Should().Contain(nameof(SkillPermission.ProcessControl));
        reviewRequired.GrantedPermissionsText.Should().Be("Granted: none");
    }

    [Fact]
    public async Task GrantPermissionsCommand_PermissionDeniedSkill_GrantsRequiredPermissionsAndRefreshesCards()
    {
        var policyManager = new RecordingSkillPolicyManager();
        var healthService = new SequencedSkillHealthService(
        [
            new SkillHealthReport
            {
                SkillId = "local.desktop-navigation",
                DisplayName = "Desktop Navigation",
                Source = "local:C:\\skills\\desktop-navigation",
                Status = SkillHealthStatus.PermissionDenied,
                Details = "Missing granted permissions: ProcessControl.",
                RequiredPermissions = [SkillPermission.ProcessControl],
                GrantedPermissions = [],
                MissingPermissions = [SkillPermission.ProcessControl]
            }
        ],
        [
            new SkillHealthReport
            {
                SkillId = "local.desktop-navigation",
                DisplayName = "Desktop Navigation",
                Source = "local:C:\\skills\\desktop-navigation",
                Status = SkillHealthStatus.Healthy,
                Details = "Executor available.",
                RequiredPermissions = [SkillPermission.ProcessControl],
                GrantedPermissions = [SkillPermission.ProcessControl],
                MissingPermissions = []
            }
        ]);
        var viewModel = new PluginsViewModel(healthService, policyManager);
        var plugin = viewModel.Plugins.Single(plugin => plugin.SkillId == "local.desktop-navigation");

        plugin.CanGrantPermissions.Should().BeTrue();
        plugin.GrantPermissionsCommand.Should().NotBeNull();
        await viewModel.GrantPermissionsAsync("local.desktop-navigation");

        policyManager.GrantedSkillIds.Should().ContainSingle().Which.Should().Be("local.desktop-navigation");
        viewModel.Plugins.Single(plugin => plugin.SkillId == "local.desktop-navigation")
            .GrantedPermissionsText.Should().Contain(nameof(SkillPermission.ProcessControl));
    }

    [Fact]
    public void Constructor_WithEvalSummary_ExposesSmokeEvalStatus()
    {
        var viewModel = new PluginsViewModel(
        [
            new SkillHealthReport
            {
                SkillId = "files.read",
                DisplayName = "Read File",
                Source = "builtin",
                Status = SkillHealthStatus.Healthy,
                Details = "Executor available."
            }
        ],
        new SkillEvalSummary
        {
            Total = 3,
            Passed = 2,
            Failed = 1,
            Results =
            [
                new SkillEvalResult
                {
                    Name = "files.exists smoke",
                    SkillId = "files.exists",
                    Passed = false,
                    ExpectedStatus = SkillExecutionStatus.Succeeded,
                    ActualStatus = SkillExecutionStatus.ValidationFailed,
                    Message = "Expected Succeeded, got ValidationFailed."
                }
            ]
        });

        viewModel.SkillEvalStatus.Should().Be("2/3 smoke evals passing");
        viewModel.SkillEvalDetail.Should().Contain("files.exists");
        viewModel.IsSkillEvalHealthy.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithEvalSummary_MapsMatchingResultsToPluginCards()
    {
        var viewModel = new PluginsViewModel(
        [
            new SkillHealthReport
            {
                SkillId = "local.desktop-navigation",
                DisplayName = "Desktop Navigation",
                Source = "local:C:\\skills\\desktop-navigation",
                Status = SkillHealthStatus.Healthy,
                Details = "Executor available."
            }
        ],
        new SkillEvalSummary
        {
            Total = 1,
            Passed = 1,
            Results =
            [
                new SkillEvalResult
                {
                    Name = "desktop navigation smoke",
                    SkillId = "local.desktop-navigation",
                    Passed = true,
                    ExpectedStatus = SkillExecutionStatus.Succeeded,
                    ActualStatus = SkillExecutionStatus.Succeeded,
                    Message = "External skill smoke passed."
                }
            ]
        });

        var plugin = viewModel.Plugins.Should().ContainSingle().Subject;
        plugin.HasEvalResult.Should().BeTrue();
        plugin.LastEvalStatus.Should().Be("Eval Pass");
        plugin.LastEvalDetail.Should().Be("External skill smoke passed.");
    }

    [Fact]
    public void Constructor_WithLastRunReport_MapsExecutionHistoryToPluginCards()
    {
        var viewModel = new PluginsViewModel(
        [
            new SkillHealthReport
            {
                SkillId = "local.desktop-navigation",
                DisplayName = "Desktop Navigation",
                Source = "local:C:\\skills\\desktop-navigation",
                Status = SkillHealthStatus.Healthy,
                Details = "Executor available.",
                LastRunAt = new DateTimeOffset(2026, 4, 26, 12, 30, 0, TimeSpan.Zero),
                LastRunStatus = SkillExecutionStatus.Failed,
                LastRunMessage = "Click target was unavailable.",
                LastRunErrorCode = "action_execution_failed",
                LastRunDurationMilliseconds = 1250
            }
        ]);

        var plugin = viewModel.Plugins.Should().ContainSingle().Subject;
        plugin.HasLastRun.Should().BeTrue();
        plugin.LastRunStatus.Should().Be("Last Run: Failed");
        plugin.LastRunDetail.Should().Contain("Click target was unavailable.");
        plugin.LastRunDetail.Should().Contain("action_execution_failed");
        plugin.LastRunDetail.Should().Contain("1250 ms");
    }

    [Fact]
    public void SelectPlugin_WithRunMetrics_ExposesReliabilitySummary()
    {
        var viewModel = new PluginsViewModel(
        [
            new SkillHealthReport
            {
                SkillId = "shell.run",
                DisplayName = "Run Shell Command",
                Source = "builtin",
                ExecutorType = "builtin",
                Status = SkillHealthStatus.Healthy,
                Details = "Executor available.",
                RecentRunCount = 4,
                RecentSuccessCount = 3,
                RecentFailureCount = 1,
                RecentSuccessRatePercent = 75,
                RecentAverageDurationMilliseconds = 40,
                LastFailureAt = new DateTimeOffset(2026, 4, 26, 14, 10, 0, TimeSpan.Zero),
                LastFailureMessage = "Command blocked.",
                LastFailureErrorCode = "shell_command_blocked"
            }
        ]);

        var plugin = viewModel.Plugins.Should().ContainSingle().Subject;
        plugin.HasHealthMetrics.Should().BeTrue();
        plugin.HealthMetricsText.Should().Be("Recent: 3/4 succeeded (75.0%) | avg 40 ms");
        plugin.HasFailureMetrics.Should().BeTrue();
        plugin.FailureMetricsText.Should().Contain("shell_command_blocked");
        plugin.FailureMetricsText.Should().Contain("Command blocked.");

        viewModel.SelectPlugin("shell.run");

        viewModel.SelectedSkillHealthMetrics.Should().Contain("3/4 succeeded");
        viewModel.SelectedSkillHealthMetrics.Should().Contain("Last Failure:");
    }

    [Fact]
    public void SelectPlugin_WithSmokeSkipReason_ExposesSmokeCoverageMetadata()
    {
        var viewModel = new PluginsViewModel(
        [
            new SkillHealthReport
            {
                SkillId = "communication.email.send",
                DisplayName = "Send Email",
                Source = "builtin",
                ExecutorType = "builtin",
                Status = SkillHealthStatus.Healthy,
                Details = "Executor available.",
                RuntimeOptions = new Dictionary<string, string>
                {
                    ["smoke.skipReason"] = "Requires configured SMTP credentials and sends a real email."
                }
            }
        ]);

        viewModel.SelectPlugin("communication.email.send");

        viewModel.SelectedSkillPolicyGuardrail.Should().Contain("Smoke coverage: not applicable");
        viewModel.SelectedSkillPolicyGuardrail.Should().Contain("SMTP credentials");
    }

    [Fact]
    public void SelectPlugin_WithRecentRuns_ExposesExecutionHistoryRows()
    {
        var viewModel = new PluginsViewModel(
        [
            new SkillHealthReport
            {
                SkillId = "shell.run",
                DisplayName = "Run Shell Command",
                Source = "builtin",
                ExecutorType = "builtin",
                Status = SkillHealthStatus.Healthy,
                Details = "Executor available.",
                RecentRuns =
                [
                    new SkillAuditRecord
                    {
                        Timestamp = new DateTimeOffset(2026, 4, 26, 14, 10, 0, TimeSpan.Zero),
                        Status = SkillExecutionStatus.PermissionDenied,
                        ErrorCode = "shell_command_blocked",
                        ResultMessage = "Command blocked.",
                        DurationMilliseconds = 3
                    },
                    new SkillAuditRecord
                    {
                        Timestamp = new DateTimeOffset(2026, 4, 26, 14, 0, 0, TimeSpan.Zero),
                        Status = SkillExecutionStatus.Succeeded,
                        ResultMessage = "Echo completed.",
                        DurationMilliseconds = 25
                    }
                ]
            }
        ]);

        viewModel.SelectPlugin("shell.run");

        var plugin = viewModel.Plugins.Should().ContainSingle().Subject;
        plugin.HasExecutionHistory.Should().BeTrue();
        viewModel.HasSelectedSkillExecutionHistory.Should().BeTrue();
        viewModel.SelectedSkillExecutionHistory.Should().HaveCount(2);
        viewModel.SelectedSkillExecutionHistory[0].StatusText.Should().Be("Permission Denied");
        viewModel.SelectedSkillExecutionHistory[0].DetailText.Should().Contain("shell_command_blocked");
        viewModel.SelectedSkillExecutionHistory[1].DetailText.Should().Contain("Echo completed.");
    }

    [Fact]
    public void SelectPlugin_UpdatesSkillDetailPanel()
    {
        var viewModel = new PluginsViewModel(
        [
            new SkillHealthReport
            {
                SkillId = "files.read",
                DisplayName = "Read File",
                Description = "Reads a local file.",
                Source = "builtin",
                ExecutorType = "builtin",
                RiskLevel = SkillRiskLevel.Low,
                Status = SkillHealthStatus.Healthy,
                Details = "Executor available.",
                Checksum = "builtin-files-read",
                RequiredPermissions = [SkillPermission.FileSystemRead],
                GrantedPermissions = [SkillPermission.FileSystemRead]
            },
            new SkillHealthReport
            {
                SkillId = "local.desktop-navigation",
                DisplayName = "Desktop Navigation",
                Description = "Imported local skill.",
                Source = "local:C:\\skills\\desktop-navigation",
                ExecutorType = "local",
                RiskLevel = SkillRiskLevel.High,
                Status = SkillHealthStatus.ReviewRequired,
                Details = "Skill requires review before it can be enabled.",
                Checksum = "abc123",
                RequiredPermissions = [SkillPermission.ProcessControl],
                GrantedPermissions = []
            }
        ]);

        viewModel.SelectPlugin("local.desktop-navigation");

        viewModel.HasSelectedPlugin.Should().BeTrue();
        viewModel.SelectedSkillId.Should().Be("local.desktop-navigation");
        viewModel.SelectedSkillTitle.Should().Be("DESKTOP NAVIGATION");
        viewModel.SelectedSkillExecutor.Should().Be("Executor: local");
        viewModel.SelectedSkillRisk.Should().Be("Risk: High");
        viewModel.SelectedSkillChecksum.Should().Be("Checksum: abc123");
        viewModel.SelectedSkillPermissions.Should().Contain(nameof(SkillPermission.ProcessControl));
    }

    [Fact]
    public async Task SaveRuntimePolicyOptionAsync_SelectedSkill_PersistsOptionAndRefreshesDetail()
    {
        var policyManager = new RecordingSkillPolicyManager();
        var healthService = new SequencedSkillHealthService(
        [
            new SkillHealthReport
            {
                SkillId = "shell.run",
                DisplayName = "Run Shell Command",
                Source = "builtin",
                ExecutorType = "builtin",
                Status = SkillHealthStatus.Healthy,
                Details = "Executor available.",
                RuntimeOptions = new Dictionary<string, string>
                {
                    ["shell.blockedPatterns"] = "git reset --hard"
                }
            }
        ],
        [
            new SkillHealthReport
            {
                SkillId = "shell.run",
                DisplayName = "Run Shell Command",
                Source = "builtin",
                ExecutorType = "builtin",
                Status = SkillHealthStatus.Healthy,
                Details = "Executor available.",
                RuntimeOptions = new Dictionary<string, string>
                {
                    ["shell.blockedPatterns"] = "git reset --hard;Remove-Item"
                }
            }
        ]);
        var viewModel = new PluginsViewModel(healthService, policyManager);

        viewModel.SelectPlugin("shell.run");
        viewModel.CanEditSelectedSkillPolicy.Should().BeTrue();
        viewModel.SelectedSkillPolicyGuardrail.Should().Contain("shell.blockedPatterns");
        viewModel.PolicyOptionKeyInput.Should().Be("shell.blockedPatterns");
        viewModel.PolicyOptionValueInput = "git reset --hard;Remove-Item";

        await viewModel.SaveRuntimePolicyOptionAsync();

        policyManager.SavedRuntimeOptions.Should().ContainSingle().Which.Should().Be((
            "shell.run",
            "shell.blockedPatterns",
            "git reset --hard;Remove-Item"));
        viewModel.SelectedSkillPolicyGuardrail.Should().Contain("Remove-Item");
        viewModel.SkillEvalStatus.Should().Be("Runtime policy saved");
    }

    [Fact]
    public async Task TestSkillAsync_WithRuntimeService_ExecutesSkillTestAndRefreshesCards()
    {
        var testService = new RecordingSkillTestService(
            SkillResult.Succeeded("Smoke test passed.") with { DurationMilliseconds = 35 });
        var healthService = new SequencedSkillHealthService(
        [
            new SkillHealthReport
            {
                SkillId = "files.exists",
                DisplayName = "Check File Exists",
                Source = "builtin",
                Status = SkillHealthStatus.Healthy,
                Details = "Executor available."
            }
        ],
        [
            new SkillHealthReport
            {
                SkillId = "files.exists",
                DisplayName = "Check File Exists",
                Source = "builtin",
                Status = SkillHealthStatus.Healthy,
                Details = "Executor available.",
                LastRunAt = new DateTimeOffset(2026, 4, 26, 13, 0, 0, TimeSpan.Zero),
                LastRunStatus = SkillExecutionStatus.Succeeded,
                LastRunMessage = "Smoke test passed.",
                LastRunDurationMilliseconds = 35
            }
        ]);
        var viewModel = new PluginsViewModel(healthService, testService);

        await viewModel.TestSkillAsync("files.exists");

        testService.TestedSkillIds.Should().ContainSingle().Which.Should().Be("files.exists");
        viewModel.SkillEvalStatus.Should().Be("Skill test passed");
        viewModel.SkillEvalDetail.Should().Be("files.exists: Smoke test passed.");
        viewModel.Plugins.Single(plugin => plugin.SkillId == "files.exists")
            .HasLastRun.Should().BeTrue();
    }

    [Fact]
    public async Task RunSkillEvalAsync_WithRuntimeServices_RefreshesSummary()
    {
        var evalHarness = new StaticSkillEvalHarness(new SkillEvalSummary
        {
            Total = 1,
            Failed = 1,
            Results =
            [
                new SkillEvalResult
                {
                    Name = "files read smoke",
                    SkillId = "files.read",
                    Passed = false,
                    ExpectedStatus = SkillExecutionStatus.Succeeded,
                    ActualStatus = SkillExecutionStatus.ValidationFailed,
                    Message = "Expected Succeeded, got ValidationFailed."
                }
            ]
        });
        var viewModel = new PluginsViewModel(
            new StaticSkillHealthService(
            [
                new SkillHealthReport
                {
                    SkillId = "files.read",
                    DisplayName = "Read File",
                    Source = "builtin",
                    Status = SkillHealthStatus.Healthy,
                    Details = "Executor available."
                }
            ]),
            evalHarness,
            new StaticSkillEvalCaseCatalog());

        await viewModel.RunSkillEvalAsync();

        viewModel.SkillEvalStatus.Should().Be("0/1 smoke evals passing");
        viewModel.SkillEvalDetail.Should().Contain("files.read");
        viewModel.IsSkillEvalHealthy.Should().BeFalse();
        viewModel.Plugins.Should().ContainSingle(plugin =>
            plugin.SkillId == "files.read"
            && plugin.HasEvalResult
            && plugin.LastEvalStatus == "Eval Fail");
        evalHarness.RunCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RunSkillEvalAsync_WithRuntimeServices_ExposesDetailedEvalRows()
    {
        var evalHarness = new StaticSkillEvalHarness(new SkillEvalSummary
        {
            Total = 2,
            Passed = 1,
            Failed = 1,
            Results =
            [
                new SkillEvalResult
                {
                    Name = "shell smoke",
                    SkillId = "shell.run",
                    Passed = true,
                    ExpectedStatus = SkillExecutionStatus.Succeeded,
                    ActualStatus = SkillExecutionStatus.Succeeded,
                    Message = "Exit Code: 0",
                    DurationMilliseconds = 12
                },
                new SkillEvalResult
                {
                    Name = "web smoke",
                    SkillId = "web.fetch",
                    Passed = false,
                    ExpectedStatus = SkillExecutionStatus.Succeeded,
                    ActualStatus = SkillExecutionStatus.PermissionDenied,
                    Message = "Host blocked.",
                    DurationMilliseconds = 2
                }
            ]
        });
        var viewModel = new PluginsViewModel(
            new StaticSkillHealthService([]),
            evalHarness,
            new StaticSkillEvalCaseCatalog());

        await viewModel.RunSkillEvalAsync();

        viewModel.HasSkillEvalResults.Should().BeTrue();
        viewModel.SkillEvalResults.Should().HaveCount(2);
        viewModel.SkillEvalResults[0].StatusText.Should().Be("Pass");
        viewModel.SkillEvalResults[0].DetailText.Should().Contain("Exit Code: 0");
        viewModel.SkillEvalResults[1].StatusText.Should().Be("Fail");
        viewModel.SkillEvalResults[1].ExpectedActualText.Should().Be("Expected: Succeeded | Actual: PermissionDenied");
        viewModel.SkillEvalResults[1].DetailText.Should().Contain("Host blocked.");
    }

    [Fact]
    public async Task ImportSkillAsync_LocalPath_ImportsSourceAndRefreshesCards()
    {
        var importService = new RecordingSkillImportService();
        var healthService = new StaticSkillHealthService(
        [
            new SkillHealthReport
            {
                SkillId = "local.desktop-navigation",
                DisplayName = "Desktop Navigation",
                Description = "Imported local skill.",
                Source = "local:C:\\skills\\desktop-navigation",
                Status = SkillHealthStatus.ReviewRequired,
                Details = "Skill requires review before it can be enabled."
            }
        ]);
        var viewModel = new PluginsViewModel(healthService, importService)
        {
            ImportLocation = " C:\\skills\\desktop-navigation ",
            SelectedImportSourceIndex = 0
        };

        await viewModel.ImportSkillAsync();

        importService.LastSource.Should().NotBeNull();
        importService.LastSource!.Kind.Should().Be(SkillSourceKind.LocalDirectory);
        importService.LastSource.Location.Should().Be("C:\\skills\\desktop-navigation");
        viewModel.ImportStatus.Should().Be("Imported 1 skill. Review required before use.");
        viewModel.Plugins.Should().Contain(plugin =>
            plugin.SkillId == "local.desktop-navigation"
            && plugin.Status == "Review Required");
    }

    [Fact]
    public async Task ImportSkillAsync_SkillsShSelection_UsesSkillsShSourceKind()
    {
        var importService = new RecordingSkillImportService();
        var viewModel = new PluginsViewModel(
            new StaticSkillHealthService([]),
            importService)
        {
            ImportLocation = "D:\\skills\\browser-control",
            SelectedImportSourceIndex = 1
        };

        await viewModel.ImportSkillAsync();

        importService.LastSource.Should().NotBeNull();
        importService.LastSource!.Kind.Should().Be(SkillSourceKind.SkillsSh);
        viewModel.ImportStatus.Should().Be("Imported 1 skill. Review required before use.");
    }

    private sealed class RecordingSkillImportService : ISkillImportService
    {
        public SkillSourceDefinition? LastSource { get; private set; }

        public Task<SkillImportResult> ImportAsync(
            SkillSourceDefinition source,
            CancellationToken cancellationToken = default)
        {
            LastSource = source;
            return Task.FromResult(new SkillImportResult
            {
                Manifests =
                [
                    new KamSkillManifest
                    {
                        Id = "local.desktop-navigation",
                        DisplayName = "Desktop Navigation"
                    }
                ]
            });
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

    private sealed class SequencedSkillHealthService : ISkillHealthService
    {
        private readonly Queue<IReadOnlyCollection<SkillHealthReport>> _reports;

        public SequencedSkillHealthService(params IReadOnlyCollection<SkillHealthReport>[] reports)
        {
            _reports = new Queue<IReadOnlyCollection<SkillHealthReport>>(reports);
        }

        public Task<IReadOnlyCollection<SkillHealthReport>> GetHealthAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_reports.Count > 1 ? _reports.Dequeue() : _reports.Peek());
        }
    }

    private sealed class RecordingSkillTestService : ISkillTestService
    {
        private readonly SkillResult _result;

        public RecordingSkillTestService(SkillResult result)
        {
            _result = result;
        }

        public List<string> TestedSkillIds { get; } = [];

        public Task<SkillResult> TestAsync(
            string skillId,
            CancellationToken cancellationToken = default)
        {
            TestedSkillIds.Add(skillId);
            return Task.FromResult(_result);
        }
    }

    private sealed class RecordingSkillPolicyManager : ISkillPolicyManager
    {
        public List<string> GrantedSkillIds { get; } = [];
        public List<(string SkillId, string Key, string Value)> SavedRuntimeOptions { get; } = [];

        public Task<bool> ApproveReviewAsync(string skillId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> EnableAsync(string skillId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> DisableAsync(string skillId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> RevokePermissionsAsync(string skillId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> GrantPermissionsAsync(string skillId, CancellationToken cancellationToken = default)
        {
            GrantedSkillIds.Add(skillId);
            return Task.FromResult(true);
        }

        public Task<bool> SetRuntimeOptionAsync(
            string skillId,
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            SavedRuntimeOptions.Add((skillId, key, value));
            return Task.FromResult(true);
        }
    }

    private sealed class StaticSkillEvalHarness : ISkillEvalHarness
    {
        private readonly SkillEvalSummary _summary;

        public StaticSkillEvalHarness(SkillEvalSummary summary)
        {
            _summary = summary;
        }

        public int RunCount { get; private set; }

        public Task<SkillEvalSummary> RunAsync(
            IEnumerable<SkillEvalCase> cases,
            CancellationToken cancellationToken = default)
        {
            RunCount++;
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
                    Name = "files read smoke",
                    Plan = SkillPlan.FromObject("files.read", new { path = "README.md" })
                }
            ];
        }
    }
}
