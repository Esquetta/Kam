using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Infrastructure.Agent.Runtime;

/// <summary>
/// A saved permission rule: a tool name glob, optionally with a pattern for the call's main argument.
/// <c>shell_run(git status:*)</c> matches <c>git status</c> and <c>git status --short</c>;
/// <c>mcp__github__*</c> matches every tool of the GitHub MCP server.
/// </summary>
public sealed class ToolPermissionRule
{
    // The argument a pattern is matched against, in order of preference.
    private static readonly string[] PrimaryArgumentNames =
        ["command", "path", "filePath", "url", "applicationName", "appName", "query", "name"];

    private static readonly Regex ShellControl = new(@"[;&|`<>\r\n]|\$\(", RegexOptions.Compiled);

    private readonly Regex _tool;
    private readonly string? _argumentPattern;

    private ToolPermissionRule(string text, Regex tool, string? argumentPattern)
    {
        Text = text;
        _tool = tool;
        _argumentPattern = argumentPattern;
    }

    /// <summary>Gets the rule as written.</summary>
    public string Text { get; }

    /// <summary>
    /// Parses a rule, or returns <c>null</c> when the text is not a rule.
    /// </summary>
    /// <param name="text">Rule text such as <c>shell_run(git status:*)</c>.</param>
    public static ToolPermissionRule? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        var open = trimmed.IndexOf('(');
        string toolPart;
        string? argumentPart = null;
        if (open >= 0)
        {
            if (!trimmed.EndsWith(')') || open == 0)
            {
                return null;
            }

            toolPart = trimmed[..open].Trim();
            argumentPart = trimmed[(open + 1)..^1].Trim();
        }
        else
        {
            toolPart = trimmed;
        }

        return toolPart.Length == 0
            ? null
            : new ToolPermissionRule(trimmed, Glob(toolPart, ignoreCase: false), argumentPart);
    }

    /// <summary>
    /// Returns whether the rule covers a call.
    /// </summary>
    /// <param name="toolName">The model-facing tool name.</param>
    /// <param name="argumentsJson">The call arguments as JSON.</param>
    /// <param name="isDenyRule">Deny rules also match any single command inside a chained shell command;
    /// allow rules never match a chained command.</param>
    public bool Matches(string toolName, string argumentsJson, bool isDenyRule)
    {
        if (!_tool.IsMatch(toolName))
        {
            return false;
        }

        if (_argumentPattern is null || _argumentPattern == "*")
        {
            return true;
        }

        var (name, value) = ReadPrimaryArgument(argumentsJson);
        if (name == "command" && ShellControl.IsMatch(value))
        {
            return isDenyRule
                && (MatchesArgument(value) || ShellControl.Split(value).Any(part => MatchesArgument(part.Trim())));
        }

        return MatchesArgument(value.Trim());
    }

    /// <summary>
    /// Builds the allow rule "Always allow" saves for a call.
    /// </summary>
    /// <param name="toolName">The model-facing tool name.</param>
    /// <param name="argumentsJson">The call arguments as JSON.</param>
    public static string Suggest(string toolName, string argumentsJson)
    {
        var (name, value) = ReadPrimaryArgument(argumentsJson);
        if (name != "command" || string.IsNullOrWhiteSpace(value))
        {
            return toolName;
        }

        // "git status --short" becomes "git status:*", "ls -la" becomes "ls:*". Only the first command of a
        // chain counts; allow rules never match chained commands anyway.
        var firstCommand = ShellControl.Split(value)[0];
        var words = firstCommand.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return toolName;
        }

        var prefix = words.Length > 1 && !words[1].StartsWith('-') ? $"{words[0]} {words[1]}" : words[0];
        return $"{toolName}({prefix}:*)";
    }

    private bool MatchesArgument(string value)
    {
        var pattern = _argumentPattern!;
        if (pattern.EndsWith(":*", StringComparison.Ordinal))
        {
            var prefix = pattern[..^2];
            return value.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || value.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase);
        }

        return Glob(pattern, ignoreCase: true).IsMatch(value.Replace('\\', '/'))
            || Glob(pattern, ignoreCase: true).IsMatch(value);
    }

    private static (string Name, string Value) ReadPrimaryArgument(string argumentsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var candidate in PrimaryArgumentNames)
                {
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (property.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)
                            && property.Value.ValueKind == JsonValueKind.String)
                        {
                            return (candidate, property.Value.GetString() ?? string.Empty);
                        }
                    }
                }
            }

            return (string.Empty, document.RootElement.GetRawText());
        }
        catch (JsonException)
        {
            return (string.Empty, argumentsJson);
        }
    }

    private static Regex Glob(string pattern, bool ignoreCase)
    {
        var expression = "^" + Regex.Escape(pattern).Replace(@"\*", ".*") + "$";
        return new Regex(expression, ignoreCase ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant : RegexOptions.CultureInvariant);
    }
}
