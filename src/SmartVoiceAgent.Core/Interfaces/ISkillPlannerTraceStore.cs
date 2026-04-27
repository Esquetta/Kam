using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ISkillPlannerTraceStore
{
    event EventHandler? Changed;

    IReadOnlyList<SkillPlannerTraceEntry> GetRecent(int maxCount = 20);

    SkillPlannerTraceEntry Record(SkillPlannerTraceEntry entry);

    void Clear();
}
