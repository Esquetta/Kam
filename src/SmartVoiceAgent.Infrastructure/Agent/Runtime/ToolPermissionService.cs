using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;

namespace SmartVoiceAgent.Infrastructure.Agent.Runtime;

/// <summary>
/// Applies the approval mode and the user's "always allow" rules, saved in
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
    public IReadOnlyCollection<string> AllowedTools
    {
        get
        {
            lock (_gate)
            {
                return _state.AllowedTools.OrderBy(name => name, StringComparer.Ordinal).ToArray();
            }
        }
    }

    /// <inheritdoc />
    public ToolPermissionDecision Evaluate(AgentToolDescriptor tool, string argumentsJson)
    {
        lock (_gate)
        {
            if (_state.AllowedTools.Contains(tool.Name))
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
    public void AlwaysAllow(string toolName)
    {
        lock (_gate)
        {
            if (_state.AllowedTools.Add(toolName))
            {
                Save();
            }
        }
    }

    /// <inheritdoc />
    public void Revoke(string toolName)
    {
        lock (_gate)
        {
            if (_state.AllowedTools.Remove(toolName))
            {
                Save();
            }
        }
    }

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

            state.AllowedTools = new HashSet<string>(state.AllowedTools, StringComparer.Ordinal);
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

        public HashSet<string> AllowedTools { get; set; } = new(StringComparer.Ordinal);
    }
}
