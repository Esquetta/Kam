namespace SmartVoiceAgent.Core.Models.Skills;

public sealed class ShellCommandResult
{
    public string Command { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    public int? ExitCode { get; init; }

    public string StdOut { get; init; } = string.Empty;

    public string StdErr { get; init; } = string.Empty;

    public bool TimedOut { get; init; }

    public bool Cancelled { get; init; }

    public bool Truncated { get; init; }

    public int MaxOutputLength { get; init; }

    public long DurationMilliseconds { get; init; }
}
