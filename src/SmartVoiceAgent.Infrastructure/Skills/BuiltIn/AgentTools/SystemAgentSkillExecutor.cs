using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Agent.Functions;

namespace SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

public sealed class SystemAgentSkillExecutor : ISkillExecutor
{
    private static readonly HashSet<string> SkillIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "apps.check",
        "apps.path",
        "apps.running",
        "apps.installed.list",
        "media.play",
        "system.device.control"
    };

    private readonly SystemAgentTools _tools;

    public SystemAgentSkillExecutor(SystemAgentTools tools)
    {
        _tools = tools;
    }

    public bool CanExecute(string skillId)
    {
        return SkillIds.Contains(skillId);
    }

    public async Task<SkillResult> ExecuteAsync(SkillPlan plan, CancellationToken cancellationToken = default)
    {
        var result = plan.SkillId.ToLowerInvariant() switch
        {
            "apps.check" => await _tools.CheckApplicationAsync(SkillPlanArgumentReader.GetString(plan, "applicationName")),
            "apps.path" => await _tools.GetApplicationPathAsync(SkillPlanArgumentReader.GetString(plan, "applicationName")),
            "apps.running" => await _tools.IsApplicationRunningAsync(SkillPlanArgumentReader.GetString(plan, "applicationName")),
            "apps.installed.list" => await _tools.ListInstalledApplicationsAsync(
                SkillPlanArgumentReader.GetBool(plan, "includeSystemApps")),
            "media.play" => await _tools.PlayMusicAsync(SkillPlanArgumentReader.GetString(plan, "trackName")),
            "system.device.control" => await _tools.ControlDeviceAsync(
                SkillPlanArgumentReader.GetString(plan, "deviceName"),
                SkillPlanArgumentReader.GetString(plan, "action")),
            _ => null
        };

        return result is null
            ? SkillResult.Failed($"Unsupported system skill: {plan.SkillId}")
            : AgentToolSkillResult.FromMessage(result);
    }
}
