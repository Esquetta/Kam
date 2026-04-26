using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ISkillPlannerService
{
    Task<SkillPlanParseResult> CreatePlanAsync(string userRequest, CancellationToken cancellationToken = default);
}
