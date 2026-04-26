using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Agent.Tools;

namespace SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

public sealed class ClipboardSkillExecutor : ISkillExecutor
{
    private static readonly HashSet<string> SkillIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "clipboard.get",
        "clipboard.set",
        "clipboard.clear"
    };

    private readonly ClipboardTools _tools;

    public ClipboardSkillExecutor(ClipboardTools tools)
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
            "clipboard.get" => await _tools.GetClipboardAsync(
                SkillPlanArgumentReader.GetInt(plan, "maxLength")),
            "clipboard.set" => await _tools.SetClipboardAsync(
                SkillPlanArgumentReader.GetString(plan, "content")),
            "clipboard.clear" => await _tools.ClearClipboardAsync(),
            _ => null
        };

        return result is null
            ? SkillResult.Failed($"Unsupported clipboard skill: {plan.SkillId}")
            : AgentToolSkillResult.FromMessage(result);
    }
}
