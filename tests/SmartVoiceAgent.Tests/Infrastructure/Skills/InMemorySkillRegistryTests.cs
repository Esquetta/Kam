using FluentAssertions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills;

public class InMemorySkillRegistryTests
{
    [Fact]
    public void Register_EnabledBuiltInSkill_CanResolveById()
    {
        ISkillRegistry registry = new InMemorySkillRegistry();
        var manifest = new KamSkillManifest
        {
            Id = "apps.open",
            DisplayName = "Open Application",
            Source = "builtin",
            ExecutorType = "builtin",
            Enabled = true,
            RiskLevel = SkillRiskLevel.High,
            Permissions = [SkillPermission.ProcessLaunch]
        };

        registry.Register(manifest);

        registry.TryGet("apps.open", out var resolved).Should().BeTrue();
        resolved!.Permissions.Should().Contain(SkillPermission.ProcessLaunch);
    }
}
