using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Actions;

public static class SkillActionPermissionPolicy
{
    public static IReadOnlyCollection<SkillPermission> GetRequiredPermissions(SkillActionPlan plan)
    {
        return plan.Actions
            .Select(MapPermission)
            .Where(permission => permission.HasValue && permission.Value != SkillPermission.None)
            .Select(permission => permission!.Value)
            .Distinct()
            .ToArray();
    }

    public static IReadOnlyCollection<SkillPermission> GetMissingPermissions(
        SkillActionPlan plan,
        IReadOnlyCollection<SkillPermission> grantedPermissions)
    {
        var granted = grantedPermissions
            .Where(permission => permission != SkillPermission.None)
            .Distinct()
            .ToHashSet();

        return GetRequiredPermissions(plan)
            .Where(permission => !granted.Contains(permission))
            .ToArray();
    }

    private static SkillPermission? MapPermission(SkillActionStep action)
    {
        return action.Type.ToLowerInvariant() switch
        {
            SkillActionTypes.OpenApp => SkillPermission.ProcessLaunch,
            SkillActionTypes.FocusWindow => SkillPermission.ProcessControl,
            SkillActionTypes.Click => SkillPermission.ProcessControl,
            SkillActionTypes.TypeText => SkillPermission.ProcessControl,
            SkillActionTypes.Hotkey => SkillPermission.ProcessControl,
            SkillActionTypes.ClipboardSet => SkillPermission.ClipboardWrite,
            SkillActionTypes.ClipboardGet => SkillPermission.ClipboardRead,
            SkillActionTypes.ReadScreen => SkillPermission.SystemInformation,
            SkillActionTypes.Respond => SkillPermission.None,
            _ => null
        };
    }
}
