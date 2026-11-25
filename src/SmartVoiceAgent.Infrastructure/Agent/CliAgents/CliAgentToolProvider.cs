using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.Agents.CliAgents;

namespace SmartVoiceAgent.Infrastructure.Agent.CliAgents;

/// <summary>
/// Offers <c>cli_agent_run</c>, which hands a task to the user's own signed-in Codex CLI, so the work bills to
/// the user's ChatGPT plan instead of an API key. The tool appears only when Codex is switched on in Integrations
/// and found on PATH. Its risk is <see cref="ToolRisk.Delegate"/>, so every run asks first: Kam cannot gate the
/// commands Codex runs itself.
/// </summary>
/// <remarks>
/// Claude Code is detected and shown in Integrations but not offered here yet: Anthropic's terms do not clearly
/// allow another app to drive a Claude plan through the CLI.
/// </remarks>
public sealed class CliAgentToolProvider : IAgentToolProvider
{
    /// <summary>The tool name.</summary>
    public const string ToolName = "cli_agent_run";

    private readonly IOptionsMonitor<CliAgentsOptions> _options;
    private readonly CodexCliAgent _codex;
    private readonly Func<string?> _workspaceRoot;
    private readonly Func<string, CliLaunch?> _locate;

    /// <summary>
    /// Creates the provider.
    /// </summary>
    /// <param name="options">Which CLIs are switched on.</param>
    /// <param name="runner">Starts the CLI.</param>
    /// <param name="workspaceRoot">The folder the user is working in, used when the model names none.</param>
    /// <param name="locate">Finds a CLI by name; defaults to <see cref="CliExecutableLocator.Find"/>.</param>
    public CliAgentToolProvider(
        IOptionsMonitor<CliAgentsOptions> options,
        ICodingAgentProcessRunner runner,
        Func<string?>? workspaceRoot = null,
        Func<string, CliLaunch?>? locate = null)
    {
        _options = options;
        _codex = new CodexCliAgent(runner);
        _workspaceRoot = workspaceRoot ?? (() => null);
        _locate = locate ?? (name => CliExecutableLocator.Find(name));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentToolDescriptor>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        var codex = options.CodexEnabled ? _locate(CliAgentDetector.CommandName(CliAgentKind.Codex)) : null;
        IReadOnlyList<AgentToolDescriptor> tools = codex is null
            ? []
            : [new AgentToolDescriptor(new RunFunction(this, codex), ToolRisk.Delegate, "cli", "Delegate to Codex CLI", PrepareArguments)];
        return Task.FromResult(tools);
    }

    /// <summary>
    /// Fills in the folder and sandbox the run will use, so the approval card shows them before the user answers.
    /// </summary>
    /// <param name="arguments">The model's arguments.</param>
    public IDictionary<string, object?> PrepareArguments(IDictionary<string, object?> arguments)
    {
        var workspace = ReadString(arguments, "workspace").Trim();
        if (workspace.Length == 0)
        {
            workspace = _workspaceRoot() ?? string.Empty;
        }

        if (Path.IsPathFullyQualified(workspace) && !IsNetworkOrDevicePath(workspace))
        {
            workspace = Path.GetFullPath(workspace);
        }

        var allowEdits = ReadBool(arguments, "allow_edits");
        var prepared = new Dictionary<string, object?>(arguments, StringComparer.Ordinal)
        {
            ["workspace"] = workspace,
            ["allow_edits"] = allowEdits,
            ["sandbox"] = allowEdits ? "workspace-write" : "read-only"
        };
        return prepared;
    }

    /// <summary>
    /// Says why a folder may not be handed to a CLI: a network or device path, a whole drive, or a system folder.
    /// </summary>
    /// <param name="workspace">A fully qualified folder path.</param>
    /// <returns>The reason, or null when the folder is allowed.</returns>
    public static string? RejectWorkspace(string workspace)
    {
        if (IsNetworkOrDevicePath(workspace))
        {
            return $"Error: {workspace} is a network or device path. Use a folder on this computer.";
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace));
        var root = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full) ?? string.Empty);
        if (string.Equals(full, root, PathComparison) || full.Length == 0)
        {
            return $"Error: {workspace} is a whole drive. Use a project folder.";
        }

        foreach (var folder in SystemFolders())
        {
            if (string.Equals(full, folder, PathComparison)
                || full.StartsWith(folder + Path.DirectorySeparatorChar, PathComparison))
            {
                return $"Error: {full} is a system folder. Use a project folder.";
            }
        }

        return null;
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool IsNetworkOrDevicePath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);

    private static IEnumerable<string> SystemFolders() =>
        new[]
            {
                Environment.SpecialFolder.Windows,
                Environment.SpecialFolder.System,
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86
            }
            .Select(Environment.GetFolderPath)
            .Where(folder => folder.Length > 0)
            .Select(Path.TrimEndingDirectorySeparator);

    private async Task<string> RunAsync(CliLaunch codex, AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var agent = ReadString(arguments, "agent");
        if (!string.IsNullOrWhiteSpace(agent) && !agent.Equals("codex", StringComparison.OrdinalIgnoreCase))
        {
            return $"Error: '{agent}' is not available. Use agent \"codex\".";
        }

        var task = ReadString(arguments, "task").Trim();
        if (task.Length == 0)
        {
            return "Error: task is required.";
        }

        var workspace = ReadString(arguments, "workspace").Trim();
        if (workspace.Length == 0)
        {
            workspace = _workspaceRoot() ?? string.Empty;
        }

        if (workspace.Length == 0 || !Path.IsPathFullyQualified(workspace))
        {
            return "Error: workspace must be the full path of a folder. Ask the user which folder to work in.";
        }

        if (RejectWorkspace(workspace) is { } rejected)
        {
            return rejected;
        }

        workspace = Path.GetFullPath(workspace);
        if (!Directory.Exists(workspace))
        {
            return $"Error: the folder {workspace} does not exist.";
        }

        var timeout = TimeSpan.FromMinutes(Math.Clamp(_options.CurrentValue.TimeoutMinutes, 1, 120));
        var request = new CliAgentRunRequest(task, workspace, ReadBool(arguments, "allow_edits"), timeout);
        var result = await _codex.RunAsync(codex, request, cancellationToken);
        return result.ToToolResult(CodexCliAgent.DisplayName);
    }

    private static string ReadString(IDictionary<string, object?> arguments, string key) =>
        arguments.TryGetValue(key, out var value) switch
        {
            false => string.Empty,
            true when value is JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
            true => value?.ToString() ?? string.Empty
        };

    private static bool ReadBool(IDictionary<string, object?> arguments, string key) =>
        arguments.TryGetValue(key, out var value) && value switch
        {
            bool flag => flag,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.String } element => bool.TryParse(element.GetString(), out var parsed) && parsed,
            string text => bool.TryParse(text, out var parsed) && parsed,
            _ => false
        };

    private sealed class RunFunction : AIFunction
    {
        private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["agent"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray("codex"),
                    ["description"] = "Which CLI runs the task."
                },
                ["task"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "A complete, self-contained brief: the goal, the files or area involved and what done looks like. The CLI does not see this conversation."
                },
                ["workspace"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Full path of the folder the CLI works in. Defaults to the user's coding workspace."
                },
                ["allow_edits"] = new JsonObject
                {
                    ["type"] = "boolean",
                    ["description"] = "Let the CLI change files inside the workspace, which must then be a git repository. Leave false to review, explain or plan without changes."
                }
            },
            ["required"] = new JsonArray("agent", "task")
        });

        private readonly CliAgentToolProvider _owner;
        private readonly CliLaunch _codex;

        public RunFunction(CliAgentToolProvider owner, CliLaunch codex)
        {
            _owner = owner;
            _codex = codex;
        }

        public override string Name => ToolName;

        public override string Description =>
            "Hands a coding task to the user's own Codex CLI, which works on it with its own model on the user's ChatGPT plan " +
            "and returns its final answer, the files it changed and the commands it ran. Use it for larger work in a code folder: " +
            "reviewing, explaining, fixing or changing code. The CLI runs read-only unless allow_edits is true, and the user approves every run. " +
            "A run can take several minutes.";

        public override JsonElement JsonSchema => Schema;

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
            await _owner.RunAsync(_codex, arguments, cancellationToken);
    }
}
