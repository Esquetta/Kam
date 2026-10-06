using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents.Extensions;

namespace SmartVoiceAgent.Infrastructure.Agent.Extensions;

/// <summary>
/// Finds Agent Skills: <c>%AppData%/Kam/skills/*/SKILL.md</c>, the workspace's <c>.kam/skills</c> and
/// <c>.claude/skills</c>, enabled plugins (named <c>plugin:skill</c>) and imported skills that were reviewed
/// and enabled. Folders are read on every call, so a new skill is available on the next turn.
/// </summary>
public sealed class AgentSkillCatalog : IAgentSkillCatalog
{
    private const int MaxDescriptionLength = 1024;
    private static readonly string[] ImportedExecutorTypes = ["local", "skills.sh"];

    private readonly IAgentPluginCatalog? _plugins;
    private readonly ISkillRegistry? _registry;
    private readonly Func<string?> _workspaceRoot;
    private readonly ILogger<AgentSkillCatalog>? _logger;

    /// <summary>
    /// Creates the catalog.
    /// </summary>
    /// <param name="userSkillsDirectory">The user's skills folder.</param>
    /// <param name="plugins">Installed plugins, for their skills.</param>
    /// <param name="registry">Imported skills.</param>
    /// <param name="workspaceRoot">Returns the current workspace, if any.</param>
    /// <param name="logger">Logger for unreadable skills.</param>
    public AgentSkillCatalog(
        string userSkillsDirectory,
        IAgentPluginCatalog? plugins = null,
        ISkillRegistry? registry = null,
        Func<string?>? workspaceRoot = null,
        ILogger<AgentSkillCatalog>? logger = null)
    {
        UserSkillsDirectory = userSkillsDirectory;
        _plugins = plugins;
        _registry = registry;
        _workspaceRoot = workspaceRoot ?? (() => null);
        _logger = logger;
    }

    /// <inheritdoc />
    public string UserSkillsDirectory { get; }

    /// <inheritdoc />
    public IReadOnlyList<AgentSkillInfo> GetSkills()
    {
        var skills = new List<AgentSkillInfo>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(AgentSkillInfo? skill)
        {
            if (skill is not null && names.Add(skill.Name))
            {
                skills.Add(skill);
            }
        }

        foreach (var directory in SkillFolders(UserSkillsDirectory))
        {
            Add(ReadSkill(directory, "user"));
        }

        foreach (var root in ExtensionPaths.WorkspaceFolders(_workspaceRoot(), "skills"))
        {
            foreach (var directory in SkillFolders(root))
            {
                Add(ReadSkill(directory, "workspace"));
            }
        }

        foreach (var plugin in SafeGetPlugins().Where(plugin => plugin.Enabled))
        {
            foreach (var directory in plugin.SkillDirectories)
            {
                Add(ReadSkill(directory, $"plugin:{plugin.Name}", $"{plugin.Name}:"));
            }
        }

        foreach (var manifest in _registry?.GetAll() ?? [])
        {
            if (!manifest.Enabled
                || manifest.ReviewRequired
                || !ImportedExecutorTypes.Contains(manifest.ExecutorType, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var separator = manifest.Source.IndexOf(':');
            var directory = separator < 0 ? manifest.Source : manifest.Source[(separator + 1)..];
            if (Directory.Exists(directory) && File.Exists(Path.Combine(directory, "SKILL.md")))
            {
                Add(ReadSkill(directory, "imported"));
            }
        }

        return skills;
    }

    /// <inheritdoc />
    public AgentSkillInfo Install(string sourceDirectory)
    {
        var source = Path.GetFullPath(sourceDirectory);
        var skill = ReadSkill(source, "user")
            ?? throw new InvalidOperationException($"No SKILL.md was found in {source}.");

        var destination = Path.Combine(UserSkillsDirectory, ExtensionPaths.ToFolderName(skill.Name));
        if (ExtensionPaths.IsInside(source, destination) || ExtensionPaths.IsInside(destination, source))
        {
            throw new InvalidOperationException("The skill is already in Kam's skills folder.");
        }

        Directory.CreateDirectory(UserSkillsDirectory);
        ExtensionPaths.CopyDirectory(source, destination);
        return skill with { Directory = destination };
    }

    /// <inheritdoc />
    public bool Uninstall(string name)
    {
        var skill = SkillFolders(UserSkillsDirectory)
            .Select(directory => ReadSkill(directory, "user"))
            .FirstOrDefault(candidate => candidate is not null && candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (skill is null || !ExtensionPaths.IsInside(UserSkillsDirectory, skill.Directory))
        {
            return false;
        }

        ExtensionPaths.DeleteDirectory(skill.Directory);
        return true;
    }

    /// <summary>
    /// Reads a skill folder's name and description.
    /// </summary>
    /// <param name="directory">The folder containing <c>SKILL.md</c>.</param>
    /// <param name="source">Where the folder was found.</param>
    /// <param name="namePrefix">Prepended to the name, such as <c>plugin:</c>.</param>
    public AgentSkillInfo? ReadSkill(string directory, string source, string namePrefix = "")
    {
        var file = Path.Combine(directory, "SKILL.md");
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            var (fields, body) = MarkdownFrontmatter.Parse(File.ReadAllText(file));
            var name = fields.TryGetValue("name", out var declared) && !string.IsNullOrWhiteSpace(declared)
                ? declared.Trim()
                : Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
            var description = fields.TryGetValue("description", out var declaredDescription) && !string.IsNullOrWhiteSpace(declaredDescription)
                ? declaredDescription.Trim()
                : FirstParagraph(body);
            if (description.Length > MaxDescriptionLength)
            {
                description = description[..MaxDescriptionLength].TrimEnd() + "…";
            }

            return new AgentSkillInfo(namePrefix + name, description, Path.GetFullPath(directory), source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not read skill {File}", file);
            return null;
        }
    }

    private IReadOnlyList<AgentPluginInfo> SafeGetPlugins()
    {
        try
        {
            return _plugins?.GetPlugins() ?? [];
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not list plugins");
            return [];
        }
    }

    private static IEnumerable<string> SkillFolders(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(root)
                .Where(directory => File.Exists(Path.Combine(directory, "SKILL.md")))
                .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string FirstParagraph(string body)
    {
        var paragraph = body
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(part => !part.StartsWith('#'));
        return paragraph is null ? string.Empty : string.Join(" ", paragraph.Split('\n', StringSplitOptions.TrimEntries));
    }
}
