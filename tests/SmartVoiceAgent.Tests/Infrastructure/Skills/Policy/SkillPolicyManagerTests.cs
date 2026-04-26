using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Policy;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.Policy;

public class SkillPolicyManagerTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _policyFile;

    public SkillPolicyManagerTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"kam-skill-policy-manager-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
        _policyFile = Path.Combine(_workspace, "skill-policies.json");
    }

    [Fact]
    public async Task ApproveReviewAsync_ClearsReviewEnablesSkillAndGrantsRequiredPermissions()
    {
        var registry = CreateRegistry(new KamSkillManifest
        {
            Id = "local.desktop-navigation",
            Source = "local:C:\\skills\\desktop-navigation",
            ExecutorType = "local",
            Enabled = false,
            ReviewRequired = true,
            Permissions = [SkillPermission.ProcessLaunch],
            GrantedPermissions = []
        });
        var manager = new SkillPolicyManager(registry, new JsonSkillPolicyStore(_policyFile));

        var result = await manager.ApproveReviewAsync("local.desktop-navigation");

        result.Should().BeTrue();
        registry.TryGet("local.desktop-navigation", out var manifest).Should().BeTrue();
        manifest!.Enabled.Should().BeTrue();
        manifest.ReviewRequired.Should().BeFalse();
        manifest.GrantedPermissions.Should().ContainSingle().Which.Should().Be(SkillPermission.ProcessLaunch);

        var persisted = new JsonSkillPolicyStore(_policyFile).GetState("local.desktop-navigation");
        persisted.Should().NotBeNull();
        persisted!.Enabled.Should().BeTrue();
        persisted.ReviewRequired.Should().BeFalse();
        persisted.GrantedPermissions.Should().ContainSingle().Which.Should().Be(SkillPermission.ProcessLaunch);
    }

    [Fact]
    public async Task DisableAsync_PersistsDisabledStateWithoutRequiringReview()
    {
        var registry = CreateRegistry(new KamSkillManifest
        {
            Id = "local.desktop-navigation",
            Source = "local:C:\\skills\\desktop-navigation",
            ExecutorType = "local",
            Enabled = true,
            ReviewRequired = false,
            Permissions = [SkillPermission.ProcessLaunch],
            GrantedPermissions = [SkillPermission.ProcessLaunch]
        });
        var manager = new SkillPolicyManager(registry, new JsonSkillPolicyStore(_policyFile));

        var result = await manager.DisableAsync("local.desktop-navigation");

        result.Should().BeTrue();
        registry.TryGet("local.desktop-navigation", out var manifest).Should().BeTrue();
        manifest!.Enabled.Should().BeFalse();
        manifest.ReviewRequired.Should().BeFalse();
        manifest.GrantedPermissions.Should().ContainSingle().Which.Should().Be(SkillPermission.ProcessLaunch);
    }

    [Fact]
    public async Task EnableAsync_DoesNotEnableSkillThatStillRequiresReview()
    {
        var registry = CreateRegistry(new KamSkillManifest
        {
            Id = "local.desktop-navigation",
            Source = "local:C:\\skills\\desktop-navigation",
            ExecutorType = "local",
            Enabled = false,
            ReviewRequired = true,
            Permissions = [SkillPermission.ProcessLaunch],
            GrantedPermissions = []
        });
        var manager = new SkillPolicyManager(registry, new JsonSkillPolicyStore(_policyFile));

        var result = await manager.EnableAsync("local.desktop-navigation");

        result.Should().BeFalse();
        registry.TryGet("local.desktop-navigation", out var manifest).Should().BeTrue();
        manifest!.Enabled.Should().BeFalse();
        manifest.ReviewRequired.Should().BeTrue();
        File.Exists(_policyFile).Should().BeFalse();
    }

    [Fact]
    public async Task RevokePermissionsAsync_RemovesGrantedPermissionsButKeepsSkillEnabled()
    {
        var registry = CreateRegistry(new KamSkillManifest
        {
            Id = "local.desktop-navigation",
            Source = "local:C:\\skills\\desktop-navigation",
            ExecutorType = "local",
            Enabled = true,
            ReviewRequired = false,
            Permissions = [SkillPermission.ProcessLaunch],
            GrantedPermissions = [SkillPermission.ProcessLaunch]
        });
        var manager = new SkillPolicyManager(registry, new JsonSkillPolicyStore(_policyFile));

        var result = await manager.RevokePermissionsAsync("local.desktop-navigation");

        result.Should().BeTrue();
        registry.TryGet("local.desktop-navigation", out var manifest).Should().BeTrue();
        manifest!.Enabled.Should().BeTrue();
        manifest.ReviewRequired.Should().BeFalse();
        manifest.GrantedPermissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GrantPermissionsAsync_GrantsRequiredPermissionsForEnabledSkill()
    {
        var registry = CreateRegistry(new KamSkillManifest
        {
            Id = "local.desktop-navigation",
            Source = "local:C:\\skills\\desktop-navigation",
            ExecutorType = "local",
            Enabled = true,
            ReviewRequired = false,
            Permissions = [SkillPermission.ProcessControl, SkillPermission.ClipboardWrite],
            GrantedPermissions = [SkillPermission.ProcessControl]
        });
        var manager = new SkillPolicyManager(registry, new JsonSkillPolicyStore(_policyFile));

        var result = await manager.GrantPermissionsAsync("local.desktop-navigation");

        result.Should().BeTrue();
        registry.TryGet("local.desktop-navigation", out var manifest).Should().BeTrue();
        manifest!.GrantedPermissions.Should().BeEquivalentTo([
            SkillPermission.ProcessControl,
            SkillPermission.ClipboardWrite]);
    }

    [Fact]
    public async Task SetRuntimeOptionAsync_PersistsOptionAndUpdatesRegistry()
    {
        var registry = CreateRegistry(new KamSkillManifest
        {
            Id = "web.fetch",
            Source = "builtin",
            ExecutorType = "builtin",
            Enabled = true,
            ReviewRequired = false
        });
        var manager = new SkillPolicyManager(registry, new JsonSkillPolicyStore(_policyFile));

        var result = await manager.SetRuntimeOptionAsync(
            "web.fetch",
            "web.allowedHosts",
            "docs.example.com");

        result.Should().BeTrue();
        registry.TryGet("web.fetch", out var manifest).Should().BeTrue();
        manifest!.RuntimeOptions.Should().Contain("web.allowedHosts", "docs.example.com");

        var persisted = new JsonSkillPolicyStore(_policyFile).GetState("web.fetch");
        persisted.Should().NotBeNull();
        persisted!.RuntimeOptions.Should().Contain("web.allowedHosts", "docs.example.com");
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private static InMemorySkillRegistry CreateRegistry(KamSkillManifest manifest)
    {
        var registry = new InMemorySkillRegistry();
        registry.Register(manifest);
        return registry;
    }
}
