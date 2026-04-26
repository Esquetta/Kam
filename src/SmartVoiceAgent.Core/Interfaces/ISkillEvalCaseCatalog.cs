using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ISkillEvalCaseCatalog
{
    IReadOnlyCollection<SkillEvalCase> CreateSmokeCases();
}
