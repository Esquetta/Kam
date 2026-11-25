using System.Text.Json;
using System.Text.RegularExpressions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents.CliAgents;

namespace SmartVoiceAgent.Infrastructure.Agent.CliAgents;

/// <summary>
/// Finds <c>codex</c> and <c>claude</c> on PATH and asks each CLI for its version and sign-in state
/// (<c>codex login status</c>, <c>claude auth status --json</c>). Kam never opens the CLIs' credential files, and
/// keeps only whether the CLI is signed in and with what kind of account, not who is signed in.
/// </summary>
public sealed partial class CliAgentDetector : ICliAgentDetector
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);

    private readonly ICodingAgentProcessRunner _runner;
    private readonly Func<string, CliLaunch?> _locate;

    /// <summary>
    /// Creates the detector.
    /// </summary>
    /// <param name="runner">Runs the CLI checks.</param>
    /// <param name="locate">Finds a CLI by name; defaults to <see cref="CliExecutableLocator.Find"/>.</param>
    public CliAgentDetector(ICodingAgentProcessRunner runner, Func<string, CliLaunch?>? locate = null)
    {
        _runner = runner;
        _locate = locate ?? (name => CliExecutableLocator.Find(name));
    }

    /// <summary>Gets the command name for a CLI.</summary>
    /// <param name="kind">The CLI.</param>
    public static string CommandName(CliAgentKind kind) => kind == CliAgentKind.Codex ? "codex" : "claude";

    /// <inheritdoc />
    public async Task<IReadOnlyList<CliAgentStatus>> DetectAsync(CancellationToken cancellationToken = default)
    {
        var checks = Enum.GetValues<CliAgentKind>().Select(kind => DetectAsync(kind, cancellationToken));
        return await Task.WhenAll(checks);
    }

    private async Task<CliAgentStatus> DetectAsync(CliAgentKind kind, CancellationToken cancellationToken)
    {
        var launch = _locate(CommandName(kind));
        if (launch is null)
        {
            return new CliAgentStatus(kind, false, null, CliAgentSignIn.Unknown);
        }

        var versionTask = RunAsync(launch, ["--version"], cancellationToken);
        var signInTask = kind == CliAgentKind.Codex
            ? RunAsync(launch, ["login", "status"], cancellationToken)
            : RunAsync(launch, ["auth", "status", "--json"], cancellationToken);
        var version = await versionTask;
        var signIn = await signInTask;

        var (state, plan) = kind == CliAgentKind.Codex ? ParseCodexLogin(signIn) : ParseClaudeAuth(signIn);
        return new CliAgentStatus(kind, true, ParseVersion(version.StandardOutput + version.StandardError), state, plan);
    }

    private Task<CodingAgentProcessResult> RunAsync(CliLaunch launch, string[] arguments, CancellationToken cancellationToken) =>
        _runner.RunAsync(
            new CodingAgentProcessRequest(launch.FileName, launch.WithArguments(arguments), Environment.CurrentDirectory, CheckTimeout),
            cancellationToken);

    /// <summary>
    /// Reads the version number from <c>--version</c> output, such as <c>codex-cli 0.161.0</c>.
    /// </summary>
    /// <param name="output">The command output.</param>
    public static string? ParseVersion(string output)
    {
        var match = VersionNumber().Match(output);
        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// Reads <c>codex login status</c>: "Logged in using ChatGPT", "Logged in using an API key" or "Not logged in".
    /// </summary>
    /// <param name="result">The command result.</param>
    public static (CliAgentSignIn SignIn, string? Plan) ParseCodexLogin(CodingAgentProcessResult result)
    {
        if (result.TimedOut || result.ExitCode == 127)
        {
            return (CliAgentSignIn.Unknown, null);
        }

        var text = result.StandardOutput + "\n" + result.StandardError;
        if (text.Contains("Not logged in", StringComparison.OrdinalIgnoreCase) || result.ExitCode != 0)
        {
            return (CliAgentSignIn.SignedOut, null);
        }

        if (text.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase))
        {
            return (CliAgentSignIn.Subscription, null);
        }

        return text.Contains("API key", StringComparison.OrdinalIgnoreCase)
            ? (CliAgentSignIn.ApiKey, null)
            : (CliAgentSignIn.Unknown, null);
    }

    /// <summary>
    /// Reads <c>claude auth status --json</c>. Only <c>loggedIn</c>, <c>authMethod</c> and <c>subscriptionType</c>
    /// are read; the account's email and organization are ignored.
    /// </summary>
    /// <param name="result">The command result.</param>
    public static (CliAgentSignIn SignIn, string? Plan) ParseClaudeAuth(CodingAgentProcessResult result)
    {
        if (result.TimedOut || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return (CliAgentSignIn.Unknown, null);
        }

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            if (!root.TryGetProperty("loggedIn", out var loggedIn) || loggedIn.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return (CliAgentSignIn.Unknown, null);
            }

            if (!loggedIn.GetBoolean())
            {
                return (CliAgentSignIn.SignedOut, null);
            }

            var method = ReadString(root, "authMethod");
            var plan = ReadString(root, "subscriptionType");
            return method.Equals("claude.ai", StringComparison.OrdinalIgnoreCase)
                ? (CliAgentSignIn.Subscription, string.IsNullOrWhiteSpace(plan) ? null : plan)
                : (CliAgentSignIn.ApiKey, null);
        }
        catch (JsonException)
        {
            return (CliAgentSignIn.Unknown, null);
        }
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    [GeneratedRegex(@"\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?")]
    private static partial Regex VersionNumber();
}
