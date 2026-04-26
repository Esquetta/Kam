using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Actions;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.Actions;

public sealed class SkillActionPermissionPolicyTests
{
    [Fact]
    public void GetRequiredPermissions_MapsDesktopActionsToPermissions()
    {
        var plan = new SkillActionPlan
        {
            Actions =
            [
                new SkillActionStep { Type = SkillActionTypes.OpenApp, ApplicationName = "notepad" },
                new SkillActionStep { Type = SkillActionTypes.FocusWindow, Target = "settings" },
                new SkillActionStep { Type = SkillActionTypes.Hotkey, Keys = ["ctrl", "l"] },
                new SkillActionStep { Type = SkillActionTypes.TypeText, Text = "hello" },
                new SkillActionStep { Type = SkillActionTypes.Click, X = 10, Y = 20 },
                new SkillActionStep { Type = SkillActionTypes.ClipboardSet, Text = "copied" },
                new SkillActionStep { Type = SkillActionTypes.ClipboardGet }
            ]
        };

        var permissions = SkillActionPermissionPolicy.GetRequiredPermissions(plan);

        permissions.Should().BeEquivalentTo([
            SkillPermission.ProcessLaunch,
            SkillPermission.ProcessControl,
            SkillPermission.ClipboardWrite,
            SkillPermission.ClipboardRead]);
    }

    [Fact]
    public void GetMissingPermissions_UsesGrantedPermissionSet()
    {
        var plan = new SkillActionPlan
        {
            Actions =
            [
                new SkillActionStep { Type = SkillActionTypes.Hotkey, Keys = ["ctrl", "l"] },
                new SkillActionStep { Type = SkillActionTypes.ClipboardGet }
            ]
        };

        var missing = SkillActionPermissionPolicy.GetMissingPermissions(
            plan,
            [SkillPermission.ProcessControl]);

        missing.Should().ContainSingle().Which.Should().Be(SkillPermission.ClipboardRead);
    }
}
