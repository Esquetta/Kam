using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ISkillEvalHarness
{
    Task<SkillEvalSummary> RunAsync(
        IEnumerable<SkillEvalCase> cases,
        CancellationToken cancellationToken = default);
}
