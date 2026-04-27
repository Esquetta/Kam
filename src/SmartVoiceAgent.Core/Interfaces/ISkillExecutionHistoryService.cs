using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ISkillExecutionHistoryService
{
    event EventHandler? Changed;

    IReadOnlyList<SkillExecutionHistoryEntry> GetRecent(int maxCount = 50);

    SkillExecutionHistoryEntry Record(
        SkillPlan plan,
        SkillResult result,
        DateTimeOffset? timestamp = null);

    void Clear();
}
