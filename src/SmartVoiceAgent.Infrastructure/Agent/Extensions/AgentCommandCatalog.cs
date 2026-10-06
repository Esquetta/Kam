using System.Text.RegularExpressions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents.Extensions;

namespace SmartVoiceAgent.Infrastructure.Agent.Extensions;

/// <summary>
/// Markdown slash commands from <c>%AppData%/Kam/commands</c>, the workspace's <c>.kam/commands</c> and
/// <c>.claude/commands</c>, and enabled plugins. <c>commands/git/pr.md</c> becomes <c>/git:pr</c>; a plugin command
/// whose name is taken is reachable as <c>/plugin:name</c>.
/// </summary>
public sealed class AgentCommandCatalog : IAgentCommandCatalog
{
    private const int MaxDescriptionLength = 120;
    private static readonly Regex Positional = new(@"\$([1-9])", RegexOptions.Compiled);
    private static readonly Regex ArgumentTokens = new("\"([^\"]*)\"|'([^']*)'|(\\S+)", RegexOptions.Compiled);

    private readonly IAgentPluginCatalog? _plugins;
    private readonly Func<string?> _workspaceRoot;

    /// <summary>
    /// Creates the catalog.
    /// </summary>
    /// <param name="userCommandsDirectory">The user's commands folder.</param>
    /// <param name="plugins">Installed plugins, for their commands.</param>
    /// <param name="workspaceRoot">Returns the current workspace, if any.</param>
    public AgentCommandCatalog(
        string userCommandsDirectory,
        IAgentPluginCatalog? plugins = null,
        Func<string?>? workspaceRoot = null)
    {
        UserCommandsDirectory = userCommandsDirectory;
        _plugins = plugins;
        _workspaceRoot = workspaceRoot ?? (() => null);
    }

    /// <inheritdoc />
    public string UserCommandsDirectory { get; }

    /// <inheritdoc />
    public IReadOnlyList<AgentCommandInfo> GetCommands()
    {
        var commands = new List<AgentCommandInfo>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string file, string source)
        {
            if (names.Add(name))
            {
                commands.Add(Read(name, file, source));
            }
        }

        foreach (var (name, file) in CommandFiles(UserCommandsDirectory))
        {
            Add(name, file, "user");
        }

        foreach (var root in ExtensionPaths.WorkspaceFolders(_workspaceRoot(), "commands"))
        {
            foreach (var (name, file) in CommandFiles(root))
            {
                Add(name, file, "workspace");
            }
        }

        foreach (var plugin in _plugins?.GetPlugins().Where(plugin => plugin.Enabled) ?? [])
        {
            foreach (var file in plugin.CommandFiles)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                Add(names.Contains(name) ? $"{plugin.Name}:{name}" : name, file, $"plugin:{plugin.Name}");
            }
        }

        return commands;
    }

    /// <inheritdoc />
    public bool TryExpand(string input, out string prompt)
    {
        prompt = string.Empty;
        var trimmed = input.Trim();
        if (!trimmed.StartsWith('/') || trimmed.Length < 2)
        {
            return false;
        }

        var space = trimmed.IndexOfAny([' ', '\t', '\n', '\r']);
        var name = space < 0 ? trimmed[1..] : trimmed[1..space];
        var arguments = space < 0 ? string.Empty : trimmed[(space + 1)..].Trim();

        var command = GetCommands().FirstOrDefault(candidate => candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (command is null || !File.Exists(command.FilePath))
        {
            return false;
        }

        var (_, body) = MarkdownFrontmatter.Parse(File.ReadAllText(command.FilePath));
        prompt = Expand(body, arguments);
        return true;
    }

    /// <summary>
    /// Fills a command body with its arguments: <c>$ARGUMENTS</c> gets all of them and <c>$1</c>…<c>$9</c> one each.
    /// When the body uses neither, the arguments are appended.
    /// </summary>
    /// <param name="body">The command's prompt.</param>
    /// <param name="arguments">What the user typed after the command.</param>
    public static string Expand(string body, string arguments)
    {
        var usesArguments = body.Contains("$ARGUMENTS", StringComparison.Ordinal);
        var usesPositional = Positional.IsMatch(body);
        var expanded = body.Replace("$ARGUMENTS", arguments, StringComparison.Ordinal);

        if (usesPositional)
        {
            var tokens = ArgumentTokens.Matches(arguments)
                .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value)
                .ToList();
            expanded = Positional.Replace(expanded, match =>
            {
                var index = match.Groups[1].Value[0] - '1';
                return index < tokens.Count ? tokens[index] : string.Empty;
            });
        }

        if (!usesArguments && !usesPositional && arguments.Length > 0)
        {
            expanded = $"{expanded.TrimEnd()}\n\nARGUMENTS: {arguments}";
        }

        return expanded.Trim();
    }

    private static AgentCommandInfo Read(string name, string file, string source)
    {
        var description = string.Empty;
        var hint = string.Empty;
        try
        {
            var (fields, body) = MarkdownFrontmatter.Parse(File.ReadAllText(file));
            description = fields.TryGetValue("description", out var declared) && !string.IsNullOrWhiteSpace(declared)
                ? declared
                : body.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.TrimStart('#', ' '))
                    .FirstOrDefault(line => line.Length > 0) ?? string.Empty;
            hint = fields.TryGetValue("argument-hint", out var declaredHint) ? declaredHint : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            description = "Could not read the command file.";
        }

        if (description.Length > MaxDescriptionLength)
        {
            description = description[..MaxDescriptionLength].TrimEnd() + "…";
        }

        return new AgentCommandInfo(name, description, hint, file, source);
    }

    private static IEnumerable<(string Name, string File)> CommandFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .Select(file => (
                    Path.ChangeExtension(Path.GetRelativePath(root, file), null)
                        .Replace(Path.DirectorySeparatorChar, ':')
                        .Replace(Path.AltDirectorySeparatorChar, ':'),
                    file))
                .Where(command => !command.Item1.Contains(' '))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
