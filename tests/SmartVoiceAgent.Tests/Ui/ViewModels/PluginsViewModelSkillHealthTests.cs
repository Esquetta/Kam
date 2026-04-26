using FluentAssertions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Adapters;
using SmartVoiceAgent.Infrastructure.Skills.Importing;
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
                Details = "Skill requires review before it can be enabled."
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
