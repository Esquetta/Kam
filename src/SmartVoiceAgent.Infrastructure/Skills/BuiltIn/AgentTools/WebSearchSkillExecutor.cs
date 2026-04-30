using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Agent.Tools;

namespace SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

public sealed class WebSearchSkillExecutor : ISkillExecutor
{
    private readonly WebSearchAgentTools _tools;

    public WebSearchSkillExecutor(WebSearchAgentTools tools)
    {
        _tools = tools;
    }

    public bool CanExecute(string skillId)
    {
        return skillId.Equals("web.search", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SkillResult> ExecuteAsync(SkillPlan plan, CancellationToken cancellationToken = default)
    {
        if (!CanExecute(plan.SkillId))
        {
            return SkillResult.Failed($"Unsupported web search skill: {plan.SkillId}");
        }

        var result = await _tools.SearchWebAsync(
            SkillPlanArgumentReader.GetString(plan, "query"),
            SkillPlanArgumentReader.GetString(plan, "lang", "tr"),
            SkillPlanArgumentReader.GetInt(plan, "results", 5),
            SkillPlanArgumentReader.GetBool(plan, "openResults"));

        return AgentToolSkillResult.FromMessage(result);
    }
}
