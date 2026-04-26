using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Agent.Tools;

namespace SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

public sealed class SystemInformationSkillExecutor : ISkillExecutor
{
    private static readonly HashSet<string> SkillIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "system.info",
        "system.cpu",
        "system.memory",
        "system.disk",
        "system.battery",
        "system.processes.list",
        "system.process.kill"
    };

    private readonly SystemInformationTools _tools;

    public SystemInformationSkillExecutor(SystemInformationTools tools)
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
            "system.info" => await _tools.GetSystemInfoAsync(),
            "system.cpu" => await _tools.GetCpuInfoAsync(),
            "system.memory" => await _tools.GetMemoryInfoAsync(),
            "system.disk" => await _tools.GetDiskInfoAsync(),
            "system.battery" => await _tools.GetBatteryStatusAsync(),
            "system.processes.list" => await _tools.ListProcessesAsync(
                SkillPlanArgumentReader.GetString(plan, "sortBy", "memory"),
                SkillPlanArgumentReader.GetInt(plan, "count", 10)),
            "system.process.kill" => await _tools.KillProcessAsync(
                SkillPlanArgumentReader.GetString(plan, "processNameOrId"),
                SkillPlanArgumentReader.GetBool(plan, "force")),
            _ => null
        };

        return result is null
            ? SkillResult.Failed($"Unsupported system information skill: {plan.SkillId}")
            : AgentToolSkillResult.FromMessage(result);
    }
}
