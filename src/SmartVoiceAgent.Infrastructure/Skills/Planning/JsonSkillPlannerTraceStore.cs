using System.Text.Json;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Core.Security;

namespace SmartVoiceAgent.Infrastructure.Skills.Planning;

public sealed class JsonSkillPlannerTraceStore : ISkillPlannerTraceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly string _filePath;

    public JsonSkillPlannerTraceStore()
        : this(CreateDefaultPath())
    {
    }

    public JsonSkillPlannerTraceStore(string filePath)
    {
        _filePath = filePath;
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

    public SkillPlannerTraceEntry Record(SkillPlannerTraceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var sanitizedEntry = Sanitize(entry);

        lock (_gate)
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(
                _filePath,
                JsonSerializer.Serialize(sanitizedEntry, JsonOptions) + Environment.NewLine);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return sanitizedEntry;
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

    private static SkillPlannerTraceEntry? TryDeserialize(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<SkillPlannerTraceEntry>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SkillPlannerTraceEntry Sanitize(SkillPlannerTraceEntry entry)
    {
        return new SkillPlannerTraceEntry
        {
            Id = entry.Id,
            Timestamp = entry.Timestamp,
            UserRequest = SecretRedactor.Redact(entry.UserRequest),
            SystemPrompt = SecretRedactor.Redact(entry.SystemPrompt),
            RawResponse = SecretRedactor.Redact(entry.RawResponse),
            IsValid = entry.IsValid,
            SkillId = entry.SkillId,
            Confidence = entry.Confidence,
            RequiresConfirmation = entry.RequiresConfirmation,
            Reasoning = SecretRedactor.Redact(entry.Reasoning),
            ErrorMessage = SecretRedactor.Redact(entry.ErrorMessage),
            DurationMilliseconds = entry.DurationMilliseconds,
            AvailableSkillCount = entry.AvailableSkillCount
        };
    }

    private static string CreateDefaultPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kam",
            "skill-planner-trace.jsonl");
    }
}
