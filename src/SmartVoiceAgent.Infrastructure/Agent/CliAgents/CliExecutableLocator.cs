using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Infrastructure.Agent.CliAgents;

/// <summary>
/// How to start a CLI found on PATH.
/// </summary>
/// <param name="FileName">The executable to start.</param>
/// <param name="PrefixArguments">Arguments that go before the CLI's own, such as the script for <c>node</c>.</param>
public sealed record CliLaunch(string FileName, IReadOnlyList<string> PrefixArguments)
{
    /// <summary>
    /// Returns the full argument list for a CLI command.
    /// </summary>
    /// <param name="arguments">The CLI's own arguments.</param>
    public IReadOnlyList<string> WithArguments(params string[] arguments) => [.. PrefixArguments, .. arguments];
}

/// <summary>
/// Finds a CLI on PATH. On Windows an npm <c>.cmd</c> shim is resolved to <c>node</c> and the package's script, so
/// arguments and the task text never pass through <c>cmd.exe</c> parsing.
/// </summary>
public static partial class CliExecutableLocator
{
    /// <summary>
    /// Finds a CLI by name.
    /// </summary>
    /// <param name="name">The command name, such as <c>codex</c>.</param>
    /// <param name="pathVariable">The PATH to search; defaults to the process's PATH.</param>
    /// <param name="isWindows">Whether to use Windows lookup rules; defaults to the current OS.</param>
    /// <returns>How to start the CLI, or null when it is not on PATH or cannot be started safely.</returns>
    public static CliLaunch? Find(string name, string? pathVariable = null, bool? isWindows = null)
    {
        var windows = isWindows ?? OperatingSystem.IsWindows();
        var directories = (pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(windows ? ';' : ':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => directory.Trim('"'));

        foreach (var directory in directories)
        {
            if (!windows)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return new CliLaunch(candidate, []);
                }

                continue;
            }

            var exe = Path.Combine(directory, name + ".exe");
            if (File.Exists(exe))
            {
                return new CliLaunch(exe, []);
            }

            var shim = Path.Combine(directory, name + ".cmd");
            if (File.Exists(shim) && TryResolveNpmShim(shim) is { } launch)
            {
                return launch;
            }
        }

        return null;
    }

    private static CliLaunch? TryResolveNpmShim(string shimPath)
    {
        string text;
        try
        {
            text = File.ReadAllText(shimPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var match = NpmShimScript().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var directory = Path.GetDirectoryName(shimPath)!;
        var script = Path.GetFullPath(Path.Combine(directory, match.Groups["script"].Value));
        if (!File.Exists(script))
        {
            return null;
        }

        var bundledNode = Path.Combine(directory, "node.exe");
        return new CliLaunch(File.Exists(bundledNode) ? bundledNode : "node", [script]);
    }

    // npm's shim ends with: "%_prog%"  "%dp0%\node_modules\@openai\codex\bin\codex.js" %*
    [GeneratedRegex("\"%dp0%\\\\(?<script>node_modules\\\\[^\"]+\\.js)\"", RegexOptions.IgnoreCase)]
    private static partial Regex NpmShimScript();
}
