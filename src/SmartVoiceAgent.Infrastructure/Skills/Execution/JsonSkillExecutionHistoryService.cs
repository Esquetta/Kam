using System.Text.Json;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Execution;

public sealed class JsonSkillExecutionHistoryService : ISkillExecutionHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly string _filePath;

    public JsonSkillExecutionHistoryService()
        : this(CreateDefaultPath())
    {
    }

    public JsonSkillExecutionHistoryService(string filePath)
    {
        _filePath = filePath;
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
            if (!File.Exists(_filePath))
            {
                return [];
            }

            return File.ReadAllLines(_filePath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(TryDeserialize)
                .Where(entry => entry is not null)
                .Select(entry => entry!)
                .Reverse()
                .Take(maxCount)
                .ToArray();
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
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(
                _filePath,
                JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static SkillExecutionHistoryEntry? TryDeserialize(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<SkillExecutionHistoryEntry>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string CreateDefaultPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kam",
            "skill-execution-history.jsonl");
    }
}
