using System.Text.Json;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Policy;

public sealed class JsonSkillPolicyStore : ISkillPolicyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _filePath;

    public JsonSkillPolicyStore()
        : this(CreateDefaultPath())
    {
    }

    public JsonSkillPolicyStore(string filePath)
    {
        _filePath = filePath;
    }

    public IReadOnlyCollection<SkillPolicyState> GetAll()
    {
        lock (_gate)
        {
            return LoadUnsafe().Values
                .OrderBy(state => state.SkillId, StringComparer.OrdinalIgnoreCase)
                .Select(Clone)
                .ToArray();
        }
    }

    public SkillPolicyState? GetState(string skillId)
    {
        lock (_gate)
        {
            return LoadUnsafe().TryGetValue(skillId, out var state)
                ? Clone(state)
                : null;
        }
    }

    public void SaveState(SkillPolicyState state)
    {
        if (string.IsNullOrWhiteSpace(state.SkillId))
        {
            throw new ArgumentException("Skill id is required.", nameof(state));
        }

        lock (_gate)
        {
            var states = LoadUnsafe();
            states[state.SkillId] = Clone(state);
            SaveUnsafe(states);
        }
    }

    public void ApplyPolicy(KamSkillManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var state = GetState(manifest.Id) ?? CreateDefaultState(manifest);
        manifest.Enabled = state.Enabled;
        manifest.ReviewRequired = state.ReviewRequired;
        manifest.GrantedPermissions = state.GrantedPermissions
            .Distinct()
            .ToList();
        manifest.RuntimeOptions = CloneRuntimeOptions(state.RuntimeOptions);
    }

    private Dictionary<string, SkillPolicyState> LoadUnsafe()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, SkillPolicyState>(StringComparer.OrdinalIgnoreCase);
        }

        var json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, SkillPolicyState>(StringComparer.OrdinalIgnoreCase);
        }

        var states = JsonSerializer.Deserialize<List<SkillPolicyState>>(json, JsonOptions) ?? [];
        return states
            .Where(state => !string.IsNullOrWhiteSpace(state.SkillId))
            .ToDictionary(
                state => state.SkillId,
                Clone,
                StringComparer.OrdinalIgnoreCase);
    }

    private void SaveUnsafe(Dictionary<string, SkillPolicyState> states)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var orderedStates = states.Values
            .OrderBy(state => state.SkillId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        File.WriteAllText(_filePath, JsonSerializer.Serialize(orderedStates, JsonOptions));
    }

    private static SkillPolicyState CreateDefaultState(KamSkillManifest manifest)
    {
        var isBuiltIn = IsBuiltIn(manifest);
        return new SkillPolicyState
        {
            SkillId = manifest.Id,
            Enabled = isBuiltIn,
            ReviewRequired = !isBuiltIn,
            GrantedPermissions = isBuiltIn
                ? manifest.Permissions.Where(permission => permission != SkillPermission.None).Distinct().ToList()
                : [],
            RuntimeOptions = CloneRuntimeOptions(manifest.RuntimeOptions)
        };
    }

    private static bool IsBuiltIn(KamSkillManifest manifest)
    {
        return manifest.Source.Equals("builtin", StringComparison.OrdinalIgnoreCase)
            || manifest.ExecutorType.Equals("builtin", StringComparison.OrdinalIgnoreCase);
    }

    private static SkillPolicyState Clone(SkillPolicyState state)
    {
        return new SkillPolicyState
        {
            SkillId = state.SkillId,
            Enabled = state.Enabled,
            ReviewRequired = state.ReviewRequired,
            GrantedPermissions = state.GrantedPermissions.Distinct().ToList(),
            RuntimeOptions = CloneRuntimeOptions(state.RuntimeOptions)
        };
    }

    private static Dictionary<string, string> CloneRuntimeOptions(
        IReadOnlyDictionary<string, string>? runtimeOptions)
    {
        if (runtimeOptions is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return runtimeOptions
            .Where(option => !string.IsNullOrWhiteSpace(option.Key))
            .ToDictionary(
                option => option.Key.Trim(),
                option => option.Value ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string CreateDefaultPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kam",
            "skill-policies.json");
    }
}
