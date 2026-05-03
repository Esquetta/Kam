using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Commands;

namespace SmartVoiceAgent.AgentHost.ConsoleApp;

public sealed class CommandSmokeOptions
{
    public string CommandText { get; init; } = CommandSmokeCommand.DefaultCommandText;

    public string? SummaryPath { get; init; }
}

public sealed class CommandSmokeCommand
{
    public const string SwitchName = "--command-smoke";
    public const string DefaultCommandText = "list applications";

    private readonly ICommandRuntimeService _runtime;

    public CommandSmokeCommand(ICommandRuntimeService runtime)
    {
        _runtime = runtime;
    }

    public static bool IsRequested(IEnumerable<string> args)
    {
        return args.Any(arg => arg.Equals(SwitchName, StringComparison.OrdinalIgnoreCase));
    }

    public static CommandSmokeOptions ParseOptions(IReadOnlyList<string> args)
    {
        var commandText = DefaultCommandText;
        var summaryPath = string.Empty;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            if (arg.Equals(SwitchName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsValueSwitch(arg, "--command", "--command-smoke-command") && index + 1 < args.Count)
            {
                commandText = args[++index];
                continue;
            }

            if (IsValueSwitch(arg, "--summary", "--command-smoke-summary") && index + 1 < args.Count)
            {
                summaryPath = args[++index];
            }
        }

        return new CommandSmokeOptions
        {
            CommandText = string.IsNullOrWhiteSpace(commandText) ? DefaultCommandText : commandText.Trim(),
            SummaryPath = string.IsNullOrWhiteSpace(summaryPath) ? null : summaryPath
        };
    }

    public async Task<int> RunAsync(
        CommandSmokeOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var result = await _runtime.ExecuteAsync(options.CommandText, cancellationToken);
        var status = result.Success ? "PASS" : "FAIL";

        await output.WriteLineAsync(
            $"[{status}] command smoke - {NormalizeMessage(options.CommandText)} -> {NormalizeMessage(result.SkillId)} ({result.DurationMilliseconds} ms)");

        if (!string.IsNullOrWhiteSpace(options.SummaryPath))
        {
            var directory = Path.GetDirectoryName(options.SummaryPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(
                options.SummaryPath,
                FormatMarkdown(options, result),
                cancellationToken);
        }

        if (result.Success && !result.RequiresConfirmation)
        {
            await output.WriteLineAsync("Command smoke completed.");
            return 0;
        }

        var reason = result.RequiresConfirmation
            ? "Command smoke requires confirmation before execution."
            : $"Command smoke failed: {NormalizeMessage(result.Message)}";
        await error.WriteLineAsync(reason);
        return 1;
    }

    private static string FormatMarkdown(CommandSmokeOptions options, CommandRuntimeResult result)
    {
        var lines = new List<string>
        {
            "# Command Smoke",
            string.Empty,
            $"- timestamp: {DateTimeOffset.Now:o}",
            $"- status: {(result.Success && !result.RequiresConfirmation ? "completed" : "failed")}",
            $"- command: {NormalizeMessage(options.CommandText)}",
            $"- skillId: {NormalizeMessage(result.SkillId)}",
            $"- runtimeStatus: {result.Status}",
            $"- success: {result.Success}",
            $"- requiresConfirmation: {result.RequiresConfirmation}",
            $"- errorCode: {NormalizeMessage(result.ErrorCode)}",
            $"- durationMilliseconds: {result.DurationMilliseconds}",
            $"- message: {NormalizeMessage(result.Message)}",
            string.Empty
        };

        return string.Join(Environment.NewLine, lines);
    }

    private static string NormalizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "(empty)";
        }

        var normalized = message
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        const int maxLength = 500;
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...";
    }

    private static bool IsValueSwitch(string arg, params string[] names)
    {
        return names.Any(name => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
