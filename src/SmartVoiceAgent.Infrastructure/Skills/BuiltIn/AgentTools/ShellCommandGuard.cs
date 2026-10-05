using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

/// <summary>
/// Spots destructive shell commands by looking at the command word of each pipeline segment,
/// so that "rm" blocks <c>rm file</c> but not <c>--verbosity normal</c> or <c>Models/</c>.
/// This is a safety net behind the approval prompt, not a sandbox.
/// </summary>
public static class ShellCommandGuard
{
    private static readonly BlockRule[] Rules =
    [
        new("rm"),
        new("unlink"),
        new("shred"),
        new("rimraf"),
        new("del"),
        new("erase"),
        new("rd"),
        new("rmdir"),
        new("remove-item"),
        new("ri"),
        new("format"),
        new("diskpart"),
        new("shutdown"),
        new("restart-computer"),
        new("stop-computer"),
        new("mkfs", CommandIsPrefix: true),
        new("dd", "if="),
        new("find", "-delete"),
        new("git", "reset", "--hard"),
        new("git", "clean", "-f|--force")
    ];

    // Tokens after which the next token is again a command word: privilege and process wrappers,
    // "cmd /c", "sh -c", "powershell -Command", PowerShell's call operator and shell keywords.
    private static readonly HashSet<string> CommandPrefixes = new(StringComparer.Ordinal)
    {
        "sudo", "doas", "nohup", "time", "env", "exec", "xargs", "npx", "pnpx", "bunx", "call", "start",
        "eval", "/c", "/k", "-c", "-command", "-exec", "-execdir", "-ok", "&", "then", "do", "else", "!"
    };

    // Shells that run their argument as a command, as in "bash -lc 'rm -rf x'" or "pwsh Remove-Item x".
    private static readonly HashSet<string> Interpreters = new(StringComparer.Ordinal)
    {
        "sh", "bash", "zsh", "dash", "ksh", "fish", "cmd", "powershell", "pwsh"
    };

    private static readonly Regex SegmentSeparators = new(@"(?:\$\(|&&|\|\||[;|`(){}\r\n])", RegexOptions.Compiled);

    /// <summary>
    /// Returns the rule a command breaks, or <c>null</c> when none applies.
    /// </summary>
    /// <param name="command">The shell command line.</param>
    public static string? FindBlockedRule(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var lower = command.ToLowerInvariant();
        if (Regex.IsMatch(lower, @":\s*\(\s*\)\s*\{"))
        {
            return "fork bomb";
        }

        foreach (var segment in SegmentSeparators.Split(lower))
        {
            var tokens = Tokenize(segment);
            for (var index = 0; index < tokens.Count; index++)
            {
                if (!IsCommandPosition(tokens, index))
                {
                    continue;
                }

                var commandWord = CommandName(tokens[index]);
                foreach (var rule in Rules)
                {
                    if (rule.Matches(commandWord, tokens, index))
                    {
                        return rule.Describe();
                    }
                }
            }
        }

        return null;
    }

    private static List<string> Tokenize(string segment)
    {
        return segment
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim('"', '\''))
            .Where(token => token.Length > 0)
            .ToList();
    }

    private static bool IsCommandPosition(IReadOnlyList<string> tokens, int index)
    {
        // Walk back over flags and "FOO=bar" assignments. The word is a command if that reaches
        // the start of the segment, a wrapper such as "sudo" or "-c", or a shell such as "bash -lc".
        for (var previous = index - 1; previous >= 0; previous--)
        {
            var token = tokens[previous];
            if (CommandPrefixes.Contains(token) || Interpreters.Contains(CommandName(token)))
            {
                return true;
            }

            if (IsAssignment(token) || IsFlag(token))
            {
                continue;
            }

            return false;
        }

        return !IsAssignment(tokens[index]);
    }

    private static bool IsFlag(string token)
    {
        return (token.Length > 1 && token[0] == '-')
            || (token.Length is 2 or 3 && token[0] == '/' && char.IsLetter(token[1]));
    }

    private static bool IsAssignment(string token)
    {
        var equals = token.IndexOf('=');
        return equals > 0 && !token.StartsWith('-') && !token.Contains('/') && !token.Contains('\\');
    }

    private static string CommandName(string token)
    {
        var name = token[(Math.Max(token.LastIndexOf('/'), token.LastIndexOf('\\')) + 1)..];
        foreach (var extension in new[] { ".exe", ".cmd", ".bat", ".ps1" })
        {
            if (name.EndsWith(extension, StringComparison.Ordinal))
            {
                return name[..^extension.Length];
            }
        }

        return name;
    }

    private sealed record BlockRule(string Command, bool CommandIsPrefix, params string[] Arguments)
    {
        public BlockRule(string command, params string[] arguments)
            : this(command, false, arguments)
        {
        }

        public bool Matches(string commandWord, IReadOnlyList<string> tokens, int commandIndex)
        {
            var commandMatches = CommandIsPrefix
                ? commandWord.StartsWith(Command, StringComparison.Ordinal)
                : commandWord.Equals(Command, StringComparison.Ordinal);
            if (!commandMatches)
            {
                return false;
            }

            // Each required argument must appear, in order, later in the same segment.
            var next = commandIndex + 1;
            foreach (var argument in Arguments)
            {
                while (next < tokens.Count && !MatchesArgument(tokens[next], argument))
                {
                    next++;
                }

                if (next >= tokens.Count)
                {
                    return false;
                }

                next++;
            }

            return true;
        }

        // "a|b" lists alternatives. A short flag such as "-f" also matches clusters like "-xdf".
        private static bool MatchesArgument(string token, string argument)
        {
            foreach (var alternative in argument.Split('|'))
            {
                if (token.StartsWith(alternative, StringComparison.Ordinal))
                {
                    return true;
                }

                if (alternative.Length == 2
                    && alternative[0] == '-'
                    && token.Length > 1
                    && token[0] == '-'
                    && token[1] != '-'
                    && token.Contains(alternative[1]))
                {
                    return true;
                }
            }

            return false;
        }

        public string Describe() =>
            Arguments.Length == 0
                ? Command
                : $"{Command} {string.Join(' ', Arguments.Select(argument => argument.Split('|')[0]))}";
    }
}
