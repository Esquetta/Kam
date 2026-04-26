using FluentAssertions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills;
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
}
