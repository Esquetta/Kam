using FluentAssertions;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills;
using SmartVoiceAgent.Infrastructure.Skills.External;
using SmartVoiceAgent.Infrastructure.Skills.Health;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.Health;

public class SkillHealthServiceTests
{
    [Fact]
    public async Task GetHealthAsync_ClassifiesRegisteredSkillsByExecutorAvailability()
    {
        var registry = new InMemorySkillRegistry();
        registry.Register(new KamSkillManifest
        {
            Id = "healthy.skill",
            DisplayName = "Healthy Skill",
            Description = "Runs correctly.",
            Source = "builtin",
            ExecutorType = "test",
            Enabled = true
        });
        registry.Register(new KamSkillManifest
        {
            Id = "missing.executor",
            DisplayName = "Missing Executor",
            Description = "Has no executor.",
            Source = "builtin",
            ExecutorType = "missing",
            Enabled = true
        });
        registry.Register(new KamSkillManifest
        {
            Id = "disabled.skill",
            DisplayName = "Disabled Skill",
            Description = "Disabled by configuration.",
            Source = "builtin",
            ExecutorType = "test",
            Enabled = false
        });
        registry.Register(new KamSkillManifest
        {
            Id = "review.required",
            DisplayName = "Review Required",
            Description = "Imported skill awaiting review.",
            Source = "local:C:\\skills\\review-required",
            ExecutorType = "local",
            Enabled = false,
            ReviewRequired = true
        });
        registry.Register(new KamSkillManifest
        {
            Id = "permission.denied",
            DisplayName = "Permission Denied",
            Description = "Needs permission grant.",
            Source = "local:C:\\skills\\permission-denied",
            ExecutorType = "local",
            Enabled = true,
            Permissions = [SkillPermission.FileSystemWrite],
            GrantedPermissions = []
        });

        var service = new SkillHealthService(registry, [new MatchingSkillExecutor("healthy.skill")]);

        var reports = await service.GetHealthAsync();

        reports.Should().HaveCount(5);
        reports.Should().BeInAscendingOrder(report => report.SkillId);

        var healthy = reports.Single(report => report.SkillId == "healthy.skill");
        healthy.Status.Should().Be(SkillHealthStatus.Healthy);
        healthy.Details.Should().Be("Executor available.");
        healthy.DisplayName.Should().Be("Healthy Skill");
        healthy.Source.Should().Be("builtin");

        var missing = reports.Single(report => report.SkillId == "missing.executor");
        missing.Status.Should().Be(SkillHealthStatus.MissingExecutor);
        missing.Details.Should().Contain("No executor");

        var disabled = reports.Single(report => report.SkillId == "disabled.skill");
        disabled.Status.Should().Be(SkillHealthStatus.Disabled);
        disabled.Details.Should().Contain("disabled");

        var reviewRequired = reports.Single(report => report.SkillId == "review.required");
        reviewRequired.Status.Should().Be(SkillHealthStatus.ReviewRequired);
        reviewRequired.Details.Should().Contain("review");

        var permissionDenied = reports.Single(report => report.SkillId == "permission.denied");
        permissionDenied.Status.Should().Be(SkillHealthStatus.PermissionDenied);
        permissionDenied.Details.Should().Contain(nameof(SkillPermission.FileSystemWrite));
    }

    [Fact]
    public async Task GetHealthAsync_ApprovedImportedExternalSkillWithExecutor_IsHealthy()
    {
        var registry = new InMemorySkillRegistry();
        registry.Register(new KamSkillManifest
        {
            Id = "local.desktop-navigation",
            DisplayName = "Desktop Navigation",
            Description = "Imported local skill.",
            Source = "local:C:\\skills\\desktop-navigation",
            ExecutorType = "local",
            Enabled = true,
            ReviewRequired = false,
            Permissions = [SkillPermission.None],
            GrantedPermissions = []
        });
        var service = new SkillHealthService(
            registry,
            [new ExternalSkillExecutor(new NoopChatClient(), registry)]);

        var reports = await service.GetHealthAsync();

        reports.Should().ContainSingle();
        var report = reports.Single();
        report.SkillId.Should().Be("local.desktop-navigation");
        report.Status.Should().Be(SkillHealthStatus.Healthy);
        report.Details.Should().Be("Executor available.");
    }

    [Fact]
    public async Task GetHealthAsync_IncludesMostRecentAuditRecordForEachSkill()
    {
        var registry = new InMemorySkillRegistry();
        registry.Register(new KamSkillManifest
        {
            Id = "local.desktop-navigation",
            DisplayName = "Desktop Navigation",
            Source = "local:C:\\skills\\desktop-navigation",
            ExecutorType = "local",
            Enabled = true,
            Permissions = [SkillPermission.None]
        });
        var latestTimestamp = new DateTimeOffset(2026, 4, 26, 12, 30, 0, TimeSpan.Zero);
        var auditLog = new StaticSkillAuditLogService(
        [
            new SkillAuditRecord
            {
                SkillId = "local.desktop-navigation",
                Timestamp = latestTimestamp.AddMinutes(-30),
                Status = SkillExecutionStatus.Succeeded,
                ResultMessage = "Older success."
            },
            new SkillAuditRecord
            {
                SkillId = "local.desktop-navigation",
                Timestamp = latestTimestamp,
                Status = SkillExecutionStatus.Failed,
                ResultMessage = "Click target was unavailable.",
                ErrorCode = "action_execution_failed",
                DurationMilliseconds = 1250
            }
        ]);
        var service = new SkillHealthService(
            registry,
            [new MatchingSkillExecutor("local.desktop-navigation")],
            auditLog);

        var reports = await service.GetHealthAsync();

        var report = reports.Should().ContainSingle().Subject;
        report.LastRunAt.Should().Be(latestTimestamp);
        report.LastRunStatus.Should().Be(SkillExecutionStatus.Failed);
        report.LastRunMessage.Should().Be("Click target was unavailable.");
        report.LastRunErrorCode.Should().Be("action_execution_failed");
        report.LastRunDurationMilliseconds.Should().Be(1250);
    }

    [Fact]
    public async Task GetHealthAsync_IncludesRecentAuditHistoryForEachSkill()
    {
        var registry = new InMemorySkillRegistry();
        registry.Register(new KamSkillManifest
        {
            Id = "shell.run",
            DisplayName = "Run Shell",
            Source = "builtin",
            ExecutorType = "builtin",
            Enabled = true
        });
        registry.Register(new KamSkillManifest
        {
            Id = "web.fetch",
            DisplayName = "Fetch URL",
            Source = "builtin",
            ExecutorType = "builtin",
            Enabled = true
        });
        var now = new DateTimeOffset(2026, 4, 26, 14, 0, 0, TimeSpan.Zero);
        var auditLog = new StaticSkillAuditLogService(
        [
            new SkillAuditRecord
            {
                SkillId = "shell.run",
                Timestamp = now.AddMinutes(-5),
                Status = SkillExecutionStatus.Succeeded,
                ResultMessage = "Echo completed.",
                DurationMilliseconds = 25
            },
            new SkillAuditRecord
            {
                SkillId = "web.fetch",
                Timestamp = now.AddMinutes(-3),
                Status = SkillExecutionStatus.PermissionDenied,
                ErrorCode = "web_host_not_allowed",
                ResultMessage = "Host not allowed."
            },
            new SkillAuditRecord
            {
                SkillId = "shell.run",
                Timestamp = now,
                Status = SkillExecutionStatus.PermissionDenied,
                ErrorCode = "shell_command_blocked",
                ResultMessage = "Command blocked.",
                DurationMilliseconds = 3
            }
        ]);
        var service = new SkillHealthService(
            registry,
            [
                new MatchingSkillExecutor("shell.run"),
                new MatchingSkillExecutor("web.fetch")
            ],
            auditLog);

        var reports = await service.GetHealthAsync();

        var shell = reports.Single(report => report.SkillId == "shell.run");
        shell.RecentRuns.Should().HaveCount(2);
        shell.RecentRuns.Select(record => record.ResultMessage)
            .Should().Equal("Command blocked.", "Echo completed.");

        var web = reports.Single(report => report.SkillId == "web.fetch");
        web.RecentRuns.Should().ContainSingle()
            .Which.ErrorCode.Should().Be("web_host_not_allowed");
    }

    private sealed class MatchingSkillExecutor : ISkillExecutor
    {
        private readonly string _skillId;

        public MatchingSkillExecutor(string skillId)
        {
            _skillId = skillId;
        }

        public bool CanExecute(string skillId)
        {
            return _skillId.Equals(skillId, StringComparison.OrdinalIgnoreCase);
        }

        public Task<SkillResult> ExecuteAsync(SkillPlan plan, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SkillResult.Succeeded("Executed."));
        }
    }

    private sealed class NoopChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return EmptyAsync();
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        public void Dispose()
        {
        }

        private static async IAsyncEnumerable<ChatResponseUpdate> EmptyAsync()
        {
            await Task.Yield();
            yield break;
        }
    }

    private sealed class StaticSkillAuditLogService : ISkillAuditLogService
    {
        private readonly IReadOnlyCollection<SkillAuditRecord> _records;

        public StaticSkillAuditLogService(IReadOnlyCollection<SkillAuditRecord> records)
        {
            _records = records;
        }

        public Task RecordAsync(
            SkillAuditRecord record,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<SkillAuditRecord>> GetRecentAsync(
            int maxCount = 100,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<SkillAuditRecord>>(
                _records
                    .OrderByDescending(record => record.Timestamp)
                    .Take(maxCount)
                    .ToArray());
        }
    }
}
