using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Adapters;

namespace SmartVoiceAgent.Infrastructure.Skills.Importing;

public interface ISkillImportService
{
    Task<SkillImportResult> ImportAsync(
        SkillSourceDefinition source,
        CancellationToken cancellationToken = default);
}
