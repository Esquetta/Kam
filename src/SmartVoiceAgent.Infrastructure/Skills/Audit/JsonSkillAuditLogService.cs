using System.Text.Json;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Audit;

public sealed class JsonSkillAuditLogService : ISkillAuditLogService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly string _filePath;

    public JsonSkillAuditLogService()
        : this(CreateDefaultPath())
    {
    }

    public JsonSkillAuditLogService(string filePath)
    {
        _filePath = filePath;
    }

    public Task RecordAsync(
        SkillAuditRecord record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(record);

        lock (_gate)
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(
                _filePath,
                JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<SkillAuditRecord>> GetRecentAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (maxCount <= 0)
        {
            return Task.FromResult<IReadOnlyCollection<SkillAuditRecord>>([]);
        }

        lock (_gate)
        {
            if (!File.Exists(_filePath))
            {
                return Task.FromResult<IReadOnlyCollection<SkillAuditRecord>>([]);
            }

            var records = File.ReadAllLines(_filePath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(TryDeserialize)
                .Where(record => record is not null)
                .Select(record => record!)
                .Reverse()
                .Take(maxCount)
                .ToArray();

            return Task.FromResult<IReadOnlyCollection<SkillAuditRecord>>(records);
        }
    }

    private static SkillAuditRecord? TryDeserialize(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<SkillAuditRecord>(line, JsonOptions);
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
            "skill-audit.jsonl");
    }
}
