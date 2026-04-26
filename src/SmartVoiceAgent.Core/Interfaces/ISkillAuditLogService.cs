using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ISkillAuditLogService
{
    Task RecordAsync(
        SkillAuditRecord record,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<SkillAuditRecord>> GetRecentAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default);
}
