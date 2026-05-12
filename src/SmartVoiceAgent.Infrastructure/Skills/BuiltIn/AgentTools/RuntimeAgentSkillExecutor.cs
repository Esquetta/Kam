using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

public sealed class RuntimeAgentSkillExecutor : ISkillExecutor
{
    public const string SkillId = "agents.run";

    private readonly IRuntimeAgentFactory _runtimeAgentFactory;

    public RuntimeAgentSkillExecutor(IRuntimeAgentFactory runtimeAgentFactory)
    {
        _runtimeAgentFactory = runtimeAgentFactory;
    }

    public bool CanExecute(string skillId)
    {
        return skillId.Equals(SkillId, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (!CanExecute(plan.SkillId))
        {
            return SkillResult.Failed($"Unsupported agent skill: {plan.SkillId}");
        }

        var task = SkillPlanArgumentReader.GetString(plan, "task");
        if (string.IsNullOrWhiteSpace(task))
        {
            return SkillResult.Failed(
                "Argument 'task' is required.",
                SkillExecutionStatus.ValidationFailed,
                "validation_failed");
        }

        var role = SkillPlanArgumentReader.GetString(plan, "role", "general");
        var agentName = SkillPlanArgumentReader.GetString(plan, "agentName");
        if (string.IsNullOrWhiteSpace(agentName))
        {
            agentName = CreateAgentName(role);
        }

        var result = await _runtimeAgentFactory
            .RunAsync(new RuntimeAgentRequest(agentName, role, task), cancellationToken)
            .ConfigureAwait(false);

        return SkillResult.Succeeded(result.Response, result);
    }

    private static string CreateAgentName(string role)
    {
        var normalized = new string(
            role
                .Where(char.IsLetterOrDigit)
                .Take(32)
                .ToArray());

        return string.IsNullOrWhiteSpace(normalized)
            ? "TaskAgent"
            : $"{normalized}Agent";
    }
}
