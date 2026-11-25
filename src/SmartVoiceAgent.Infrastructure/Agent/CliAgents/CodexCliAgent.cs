using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SmartVoiceAgent.Infrastructure.Agent.CliAgents;

/// <summary>
/// One task for a coding-agent CLI.
/// </summary>
/// <param name="Task">What the CLI should do, in plain language.</param>
/// <param name="Workspace">The folder the CLI works in.</param>
/// <param name="AllowEdits">Whether the CLI may change files in the workspace; otherwise it runs read-only.</param>
/// <param name="Timeout">How long the run may take.</param>
public sealed record CliAgentRunRequest(string Task, string Workspace, bool AllowEdits, TimeSpan Timeout);

/// <summary>
/// What a CLI run produced.
/// </summary>
/// <param name="Success">Whether the run finished without an error.</param>
/// <param name="FinalMessage">The CLI's last answer.</param>
/// <param name="ChangedFiles">Files the CLI reported changing, as "kind path".</param>
/// <param name="Commands">Shell commands the CLI ran, with their exit codes.</param>
/// <param name="Usage">Token usage the CLI reported, if any.</param>
/// <param name="Error">Why the run failed, when it did.</param>
public sealed record CliAgentRunResult(
    bool Success,
    string FinalMessage,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> Commands,
    string? Usage,
    string? Error)
{
    private const int MaxListed = 40;

    /// <summary>
    /// Formats the result for the model.
    /// </summary>
    /// <param name="agentName">The CLI's display name.</param>
    public string ToToolResult(string agentName)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Success ? $"{agentName} finished." : $"Error: {agentName} did not finish: {Error}");

        if (!string.IsNullOrWhiteSpace(FinalMessage))
        {
            builder.AppendLine().AppendLine("Final answer:").AppendLine(FinalMessage.Trim());
        }

        AppendList(builder, "Files changed", ChangedFiles, "none");
        if (Commands.Count > 0)
        {
            AppendList(builder, "Commands run", Commands, "none");
        }

        if (!string.IsNullOrWhiteSpace(Usage))
        {
            builder.AppendLine().Append("Usage: ").AppendLine(Usage);
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendList(StringBuilder builder, string title, IReadOnlyList<string> items, string empty)
    {
        builder.AppendLine().Append(title).Append(": ");
        if (items.Count == 0)
        {
            builder.AppendLine(empty);
            return;
        }

        builder.AppendLine();
        foreach (var item in items.Take(MaxListed))
        {
            builder.Append("- ").AppendLine(item);
        }

        if (items.Count > MaxListed)
        {
            builder.AppendLine($"- …and {items.Count - MaxListed} more");
        }
    }
}

/// <summary>
/// Runs a task with <c>codex exec --json</c> on the user's installed, signed-in Codex CLI. The task goes in on
/// standard input, the run uses Codex's read-only sandbox unless edits are allowed (then workspace-write), and the
/// JSONL event stream is reduced to the final answer, changed files and commands.
/// </summary>
public sealed class CodexCliAgent
{
    /// <summary>The name shown to the model and the user.</summary>
    public const string DisplayName = "Codex CLI";

    private const int MaxErrorCharacters = 1500;

    private readonly ICodingAgentProcessRunner _runner;

    /// <summary>
    /// Creates the runner.
    /// </summary>
    /// <param name="runner">Starts the CLI.</param>
    public CodexCliAgent(ICodingAgentProcessRunner runner)
    {
        _runner = runner;
    }

    /// <summary>
    /// Builds the <c>codex</c> arguments for a request; the task itself is sent on standard input. A read-only run
    /// may use any folder. A run that may edit keeps Codex's git repository check, so its changes can be reviewed
    /// and undone with git.
    /// </summary>
    /// <param name="request">The task.</param>
    public static string[] BuildArguments(CliAgentRunRequest request) =>
    [
        "exec",
        "--json",
        .. request.AllowEdits ? Array.Empty<string>() : ["--skip-git-repo-check"],
        "--sandbox",
        request.AllowEdits ? "workspace-write" : "read-only",
        "--cd",
        request.Workspace,
        "-"
    ];

    /// <summary>
    /// Runs the task.
    /// </summary>
    /// <param name="launch">How to start the CLI.</param>
    /// <param name="request">The task.</param>
    /// <param name="cancellationToken">Stops the CLI.</param>
    public async Task<CliAgentRunResult> RunAsync(
        CliLaunch launch,
        CliAgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var process = await _runner.RunAsync(
            new CodingAgentProcessRequest(
                launch.FileName,
                launch.WithArguments(BuildArguments(request)),
                request.Workspace,
                request.Timeout,
                request.Task),
            cancellationToken);

        return ParseEvents(process, request.Workspace, request.Timeout);
    }

    /// <summary>
    /// Reduces the <c>codex exec --json</c> event stream to a result.
    /// </summary>
    /// <param name="process">The finished process.</param>
    /// <param name="workspace">Used to show changed files relative to the workspace.</param>
    /// <param name="timeout">The run's time limit, named in the error when it ran out.</param>
    public static CliAgentRunResult ParseEvents(CodingAgentProcessResult process, string workspace, TimeSpan timeout)
    {
        var finalMessage = string.Empty;
        var changed = new List<string>();
        var commands = new List<string>();
        string? usage = null;
        string? error = null;

        foreach (var line in process.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith('{'))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                switch (ReadString(root, "type"))
                {
                    case "item.completed" when root.TryGetProperty("item", out var item):
                        ReadItem(item, workspace, ref finalMessage, changed, commands);
                        break;
                    case "turn.completed" when root.TryGetProperty("usage", out var tokens):
                        usage = FormatUsage(tokens);
                        break;
                    case "turn.failed" when root.TryGetProperty("error", out var failure):
                        error = ReadString(failure, "message");
                        break;
                    case "error":
                        error = ReadString(root, "message");
                        break;
                }
            }
            catch (JsonException)
            {
                // Codex prints the odd non-JSON line; the events around it still count.
            }
        }

        if (process.TimedOut)
        {
            error = $"it ran longer than {timeout.TotalMinutes.ToString("0.#", CultureInfo.InvariantCulture)} minutes and was stopped";
        }
        else if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(error))
        {
            error = $"exit code {process.ExitCode}. {Tail(process.StandardError)}".Trim();
        }

        var success = !process.TimedOut && process.ExitCode == 0 && string.IsNullOrWhiteSpace(error);
        return new CliAgentRunResult(success, finalMessage, changed.Distinct().ToList(), commands, usage, success ? null : error);
    }

    private static void ReadItem(
        JsonElement item,
        string workspace,
        ref string finalMessage,
        List<string> changed,
        List<string> commands)
    {
        var type = ReadString(item, "type");
        if (string.IsNullOrEmpty(type))
        {
            type = ReadString(item, "item_type");
        }

        switch (type)
        {
            case "agent_message":
                var text = ReadString(item, "text");
                if (!string.IsNullOrWhiteSpace(text))
                {
                    finalMessage = text;
                }

                break;
            case "command_execution":
                var command = ReadString(item, "command");
                if (!string.IsNullOrWhiteSpace(command))
                {
                    commands.Add(item.TryGetProperty("exit_code", out var exit) && exit.ValueKind == JsonValueKind.Number
                        ? $"{command} (exit {exit.GetInt32()})"
                        : command);
                }

                break;
            case "file_change" when item.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Array:
                foreach (var change in changes.EnumerateArray())
                {
                    var path = ReadString(change, "path");
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        var kind = ReadString(change, "kind");
                        var shown = Relative(workspace, path);
                        changed.Add(string.IsNullOrWhiteSpace(kind) ? shown : $"{kind} {shown}");
                    }
                }

                break;
        }
    }

    private static string? FormatUsage(JsonElement usage)
    {
        static long Read(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : 0;

        var input = Read(usage, "input_tokens");
        var cached = Read(usage, "cached_input_tokens");
        var output = Read(usage, "output_tokens");
        return input == 0 && output == 0
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"{input:N0} input tokens ({cached:N0} cached), {output:N0} output tokens, billed to the Codex CLI's account");
    }

    private static string Relative(string workspace, string path)
    {
        try
        {
            var full = Path.GetFullPath(path, workspace);
            var relative = Path.GetRelativePath(workspace, full);
            return relative.StartsWith("..", StringComparison.Ordinal) ? full : relative.Replace('\\', '/');
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private static string Tail(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= MaxErrorCharacters ? trimmed : "…" + trimmed[^MaxErrorCharacters..];
    }

    private static string ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
