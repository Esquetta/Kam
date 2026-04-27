using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Planning;

public sealed class InMemorySkillPlannerTraceStore : ISkillPlannerTraceStore
{
    private const int DefaultMaxEntries = 200;

    private readonly object _gate = new();
    private readonly int _maxEntries;
    private readonly List<SkillPlannerTraceEntry> _entries = [];

    public InMemorySkillPlannerTraceStore(int maxEntries = DefaultMaxEntries)
    {
        _maxEntries = Math.Max(1, maxEntries);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<SkillPlannerTraceEntry> GetRecent(int maxCount = 20)
    {
        if (maxCount <= 0)
        {
            return [];
        }

        lock (_gate)
        {
            return _entries
                .OrderByDescending(entry => entry.Timestamp)
                .Take(maxCount)
                .ToArray();
        }
    }

    public SkillPlannerTraceEntry Record(SkillPlannerTraceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > _maxEntries)
            {
                _entries.RemoveRange(0, _entries.Count - _maxEntries);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
