using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Policy;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.Policy;

public class SkillPolicyStoreTests : IDisposable
{
    private readonly string _policyFile;

    public SkillPolicyStoreTests()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"kam-skill-policy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        _policyFile = Path.Combine(workspace, "skill-policies.json");
    }

    [Fact]
    public void ApplyPolicy_BuiltInSkillDefaultsToEnabledAndGrantedPermissions()
    {
        var store = new JsonSkillPolicyStore(_policyFile);
        var manifest = new KamSkillManifest
        {
            Id = "apps.open",
            Source = "builtin",
            ExecutorType = "builtin",
            Enabled = false,
            Permissions = [SkillPermission.ProcessLaunch]
        };

        store.ApplyPolicy(manifest);

        manifest.Enabled.Should().BeTrue();
        manifest.ReviewRequired.Should().BeFalse();
        manifest.GrantedPermissions.Should().ContainSingle().Which.Should().Be(SkillPermission.ProcessLaunch);
    }

    [Fact]
    public void ApplyPolicy_ImportedSkillDefaultsToDisabledReviewRequiredAndNoGrantedPermissions()
    {
        var store = new JsonSkillPolicyStore(_policyFile);
        var manifest = new KamSkillManifest
        {
            Id = "local.desktop-navigation",
            Source = "local:C:\\skills\\desktop-navigation",
            ExecutorType = "local",
            Enabled = true,
            Permissions = [SkillPermission.ProcessLaunch]
        };

        store.ApplyPolicy(manifest);

        manifest.Enabled.Should().BeFalse();
        manifest.ReviewRequired.Should().BeTrue();
        manifest.GrantedPermissions.Should().BeEmpty();
    }

    [Fact]
    public void SaveState_PersistsUserPolicyAcrossStoreInstances()
    {
        var firstStore = new JsonSkillPolicyStore(_policyFile);
        firstStore.SaveState(new SkillPolicyState
        {
            SkillId = "local.desktop-navigation",
            Enabled = true,
            ReviewRequired = false,
            GrantedPermissions = [SkillPermission.ProcessLaunch]
        });

        var secondStore = new JsonSkillPolicyStore(_policyFile);
        var manifest = new KamSkillManifest
        {
            Id = "local.desktop-navigation",
            Source = "local:C:\\skills\\desktop-navigation",
            ExecutorType = "local",
            Permissions = [SkillPermission.ProcessLaunch]
        };

        secondStore.ApplyPolicy(manifest);

        manifest.Enabled.Should().BeTrue();
        manifest.ReviewRequired.Should().BeFalse();
        manifest.GrantedPermissions.Should().ContainSingle().Which.Should().Be(SkillPermission.ProcessLaunch);
    }

    [Fact]
    public void SaveState_PersistsRuntimeOptionsAcrossStoreInstances()
    {
        var firstStore = new JsonSkillPolicyStore(_policyFile);
        firstStore.SaveState(new SkillPolicyState
        {
            SkillId = "shell.run",
            Enabled = true,
            ReviewRequired = false,
            RuntimeOptions = new Dictionary<string, string>
            {
                ["shell.blockedPatterns"] = "git reset --hard",
                ["shell.allowedCommands"] = "dotnet;git status"
            }
        });

        var secondStore = new JsonSkillPolicyStore(_policyFile);
        var manifest = new KamSkillManifest
        {
            Id = "shell.run",
            Source = "builtin",
            ExecutorType = "builtin"
        };

        secondStore.ApplyPolicy(manifest);

        manifest.RuntimeOptions.Should().Contain("shell.blockedPatterns", "git reset --hard");
        manifest.RuntimeOptions.Should().Contain("shell.allowedCommands", "dotnet;git status");
    }

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_policyFile);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
