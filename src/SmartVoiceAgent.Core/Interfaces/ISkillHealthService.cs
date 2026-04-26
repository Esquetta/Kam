using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ISkillHealthService
{
    Task<IReadOnlyCollection<SkillHealthReport>> GetHealthAsync(CancellationToken cancellationToken = default);
}
