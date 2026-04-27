using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Execution;

public sealed class InMemorySkillExecutionHistoryService : ISkillExecutionHistoryService
{
    private const int DefaultMaxEntries = 100;

    private readonly object _gate = new();
    private readonly List<SkillExecutionHistoryEntry> _entries = [];
    private readonly int _maxEntries;

    public InMemorySkillExecutionHistoryService(int maxEntries = DefaultMaxEntries)
    {
        _maxEntries = Math.Max(1, maxEntries);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<SkillExecutionHistoryEntry> GetRecent(int maxCount = 50)
    {
        if (maxCount <= 0)
        {
            return [];
        }

        lock (_gate)
        {
            return _entries.Take(maxCount).ToArray();
        }
    }

    public SkillExecutionHistoryEntry Record(
        SkillPlan plan,
        SkillResult result,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);

        var entry = SkillExecutionHistoryEntryFactory.Create(plan, result, timestamp);

        lock (_gate)
        {
            _entries.Insert(0, entry);
            if (_entries.Count > _maxEntries)
            {
                _entries.RemoveRange(_maxEntries, _entries.Count - _maxEntries);
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
