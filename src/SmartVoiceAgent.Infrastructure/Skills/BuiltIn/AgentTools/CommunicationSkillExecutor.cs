using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Agent.Tools;

namespace SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

public sealed class CommunicationSkillExecutor : ISkillExecutor
{
    private static readonly HashSet<string> SkillIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "communication.email.send",
        "communication.email.template.send",
        "communication.email.validate",
        "communication.sms.send",
        "communication.sms.validate",
        "communication.sms.status"
    };

    private readonly CommunicationAgentTools _tools;

    public CommunicationSkillExecutor(CommunicationAgentTools tools)
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
            "communication.email.send" => await _tools.SendEmailAsync(
                SkillPlanArgumentReader.GetString(plan, "to"),
                SkillPlanArgumentReader.GetString(plan, "subject"),
                SkillPlanArgumentReader.GetString(plan, "body"),
                SkillPlanArgumentReader.GetBool(plan, "isHtml")),
            "communication.email.template.send" => await _tools.SendEmailTemplateAsync(
                SkillPlanArgumentReader.GetString(plan, "to"),
                SkillPlanArgumentReader.GetString(plan, "templateName"),
                SkillPlanArgumentReader.GetString(plan, "templateDataJson")),
            "communication.email.validate" => _tools.ValidateEmail(SkillPlanArgumentReader.GetString(plan, "email")),
            "communication.sms.send" => await _tools.SendSmsAsync(
                SkillPlanArgumentReader.GetString(plan, "to"),
                SkillPlanArgumentReader.GetString(plan, "message")),
            "communication.sms.validate" => _tools.ValidatePhoneNumber(SkillPlanArgumentReader.GetString(plan, "phoneNumber")),
            "communication.sms.status" => await _tools.CheckSmsConnectionAsync(),
            _ => null
        };

        return result is null
            ? SkillResult.Failed($"Unsupported communication skill: {plan.SkillId}")
            : AgentToolSkillResult.FromMessage(result);
    }
}
