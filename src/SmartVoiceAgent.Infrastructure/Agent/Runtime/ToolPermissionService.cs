using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;

namespace SmartVoiceAgent.Infrastructure.Agent.Runtime;

/// <summary>
/// Applies the user's deny rules, then allow rules, then the approval mode. Saved in
/// <c>%AppData%/Kam/agent-permissions.json</c>.
/// </summary>
public sealed class ToolPermissionService : IToolPermissionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _filePath;
    private PermissionState _state;

    /// <summary>
    /// Creates the service with the default file location.
    /// </summary>
    /// <param name="options">Runtime options; supplies the default mode when nothing is saved yet.</param>
    public ToolPermissionService(IOptions<AgentRuntimeOptions> options)
        : this(DefaultPath(), options.Value.ApprovalMode)
    {
    }

    /// <summary>
    /// Creates the service with an explicit file location.
    /// </summary>
    /// <param name="filePath">Where rules are saved.</param>
    /// <param name="defaultMode">Mode used when the file does not exist.</param>
    public ToolPermissionService(string filePath, ApprovalMode defaultMode = ApprovalMode.Ask)
    {
        _filePath = filePath;
        _state = Load(filePath) ?? new PermissionState { Mode = defaultMode };
    }

    /// <inheritdoc />
    public ApprovalMode Mode
    {
        get
        {
            lock (_gate)
            {
                return _state.Mode;
            }
        }
        set
        {
            lock (_gate)
            {
                _state.Mode = value;
                Save();
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> AllowRules
    {
        get
        {
            lock (_gate)
            {
                return _state.AllowedTools.OrderBy(rule => rule, StringComparer.Ordinal).ToArray();
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> DenyRules
    {
        get
        {
            lock (_gate)
            {
                return _state.DeniedTools.OrderBy(rule => rule, StringComparer.Ordinal).ToArray();
            }
        }
    }

    /// <inheritdoc />
    public ToolPermissionDecision Evaluate(AgentToolDescriptor tool, string argumentsJson)
    {
        lock (_gate)
        {
            if (Matches(_state.DeniedTools, tool.Name, argumentsJson, isDenyRule: true))
            {
                return ToolPermissionDecision.Deny;
            }

            if (Matches(_state.AllowedTools, tool.Name, argumentsJson, isDenyRule: false))
            {
                return ToolPermissionDecision.Allow;
            }

            return (_state.Mode, tool.Risk) switch
            {
                (ApprovalMode.FullAuto, _) => ToolPermissionDecision.Allow,
                (_, ToolRisk.Read or ToolRisk.Network) => ToolPermissionDecision.Allow,
                (ApprovalMode.AutoEdit, ToolRisk.Write) => ToolPermissionDecision.Allow,
                _ => ToolPermissionDecision.Ask
            };
        }
    }

    /// <inheritdoc />
    public string SuggestAllowRule(AgentToolDescriptor tool, string argumentsJson) =>
        ToolPermissionRule.Suggest(tool.Name, argumentsJson);

    /// <inheritdoc />
    public void AddRule(string rule, bool allow)
    {
        var parsed = ToolPermissionRule.TryParse(rule)
            ?? throw new ArgumentException($"'{rule}' is not a permission rule.", nameof(rule));

        lock (_gate)
        {
            (allow ? _state.DeniedTools : _state.AllowedTools).Remove(parsed.Text);
            if ((allow ? _state.AllowedTools : _state.DeniedTools).Add(parsed.Text))
            {
                Save();
            }
        }
    }

    /// <inheritdoc />
    public void RemoveRule(string rule)
    {
        lock (_gate)
        {
            var removed = _state.AllowedTools.Remove(rule.Trim()) | _state.DeniedTools.Remove(rule.Trim());
            if (removed)
            {
                Save();
            }
        }
    }

    private static bool Matches(IEnumerable<string> rules, string toolName, string argumentsJson, bool isDenyRule) =>
        rules.Any(rule => ToolPermissionRule.TryParse(rule)?.Matches(toolName, argumentsJson, isDenyRule) == true);

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_filePath, JsonSerializer.Serialize(_state, JsonOptions));
        }
        catch (IOException)
        {
            // Rules still apply for this session when the file cannot be written.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static PermissionState? Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var state = JsonSerializer.Deserialize<PermissionState>(File.ReadAllText(filePath), JsonOptions);
            if (state is null)
            {
                return null;
            }

            state.AllowedTools = new HashSet<string>(state.AllowedTools ?? [], StringComparer.Ordinal);
            state.DeniedTools = new HashSet<string>(state.DeniedTools ?? [], StringComparer.Ordinal);
            return state;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Kam",
        "agent-permissions.json");

    private sealed class PermissionState
    {
        public ApprovalMode Mode { get; set; } = ApprovalMode.Ask;

        // Allow rules; the name predates argument patterns and is kept so saved files still load.
        public HashSet<string> AllowedTools { get; set; } = new(StringComparer.Ordinal);

        public HashSet<string> DeniedTools { get; set; } = new(StringComparer.Ordinal);
    }
}
