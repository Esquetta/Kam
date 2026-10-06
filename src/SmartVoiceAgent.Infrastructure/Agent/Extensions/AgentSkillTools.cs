using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.Agents.Extensions;

namespace SmartVoiceAgent.Infrastructure.Agent.Extensions;

/// <summary>
/// Offers <c>load_skill</c>, which returns a skill's instructions or one of its files. The system prompt lists
/// only names and descriptions, so a skill costs context only once the model decides it needs it.
/// </summary>
public sealed class AgentSkillToolProvider : IAgentToolProvider
{
    /// <summary>The tool name.</summary>
    public const string ToolName = "load_skill";

    private readonly IAgentSkillCatalog _catalog;

    /// <summary>
    /// Creates the provider.
    /// </summary>
    /// <param name="catalog">The skills.</param>
    public AgentSkillToolProvider(IAgentSkillCatalog catalog)
    {
        _catalog = catalog;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentToolDescriptor>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        var skills = _catalog.GetSkills();
        IReadOnlyList<AgentToolDescriptor> tools = skills.Count == 0
            ? []
            : [new AgentToolDescriptor(new LoadSkillFunction(skills), ToolRisk.Read, "skills", "Load skill")];
        return Task.FromResult(tools);
    }

    private sealed class LoadSkillFunction : AIFunction
    {
        private const int MaxFileCharacters = 60000;
        private const int MaxListedFiles = 60;

        private readonly IReadOnlyList<AgentSkillInfo> _skills;
        private readonly JsonElement _schema;

        public LoadSkillFunction(IReadOnlyList<AgentSkillInfo> skills)
        {
            _skills = skills;
            var names = new JsonArray(skills.Select(skill => (JsonNode)JsonValue.Create(skill.Name)!).ToArray());
            _schema = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["name"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "The skill name, as listed in the system prompt.",
                        ["enum"] = names
                    },
                    ["file"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Optional. A file inside the skill folder that its instructions refer to, such as reference.md."
                    }
                },
                ["required"] = new JsonArray("name")
            });
        }

        public override string Name => ToolName;

        public override string Description =>
            "Loads a skill's instructions. Call it before starting a task that matches a skill listed in the system prompt, " +
            "then follow the instructions. Pass file to read another file the instructions refer to.";

        public override JsonElement JsonSchema => _schema;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            var name = ReadString(arguments, "name");
            var skill = _skills.FirstOrDefault(candidate => candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (skill is null)
            {
                return $"Error: there is no skill named '{name}'. Available: {string.Join(", ", _skills.Select(candidate => candidate.Name))}.";
            }

            var file = ReadString(arguments, "file");
            if (!string.IsNullOrWhiteSpace(file))
            {
                var path = Path.GetFullPath(Path.Combine(skill.Directory, file));
                if (!ExtensionPaths.IsInside(skill.Directory, path))
                {
                    return "Error: the file must be inside the skill folder.";
                }

                if (!File.Exists(path))
                {
                    return $"Error: {file} does not exist in the skill folder.";
                }

                var content = await File.ReadAllTextAsync(path, cancellationToken);
                return content.Length <= MaxFileCharacters
                    ? content
                    : content[..MaxFileCharacters] + "\n...[truncated]";
            }

            var (_, body) = MarkdownFrontmatter.Parse(await File.ReadAllTextAsync(skill.SkillFile, cancellationToken));
            var builder = new StringBuilder()
                .AppendLine($"Skill: {skill.Name}")
                .AppendLine($"Folder: {skill.Directory}")
                .AppendLine()
                .AppendLine(body);

            var files = Directory.EnumerateFiles(skill.Directory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(skill.Directory, path).Replace('\\', '/'))
                .Where(path => !path.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase) && !path.StartsWith(".git/", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Take(MaxListedFiles + 1)
                .ToList();
            if (files.Count > 0)
            {
                builder.AppendLine()
                    .Append("Other files in this skill (read with load_skill and file, or run scripts from the folder): ")
                    .Append(string.Join(", ", files.Take(MaxListedFiles)))
                    .AppendLine(files.Count > MaxListedFiles ? ", …" : string.Empty);
            }

            return builder.ToString().TrimEnd();
        }

        private static string ReadString(AIFunctionArguments arguments, string key) =>
            arguments.TryGetValue(key, out var value) switch
            {
                false => string.Empty,
                true when value is JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
                true => value?.ToString() ?? string.Empty
            };
    }
}

/// <summary>
/// Lists the available skills in the system prompt, one line each.
/// </summary>
public sealed class AgentSkillPromptContributor : IAgentPromptContributor
{
    private const int MaxListedSkills = 100;
    private const int MaxLineDescription = 300;

    private readonly IAgentSkillCatalog _catalog;

    /// <summary>
    /// Creates the contributor.
    /// </summary>
    /// <param name="catalog">The skills.</param>
    public AgentSkillPromptContributor(IAgentSkillCatalog catalog)
    {
        _catalog = catalog;
    }

    /// <inheritdoc />
    public string? GetPromptSection()
    {
        var skills = _catalog.GetSkills();
        if (skills.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder()
            .AppendLine("Skills:")
            .AppendLine($"Skills are instructions for specific tasks. When a task matches one, call {AgentSkillToolProvider.ToolName} with its name before you start, then follow it.");
        foreach (var skill in skills.Take(MaxListedSkills))
        {
            var description = skill.Description.Length > MaxLineDescription
                ? skill.Description[..MaxLineDescription].TrimEnd() + "…"
                : skill.Description;
            builder.AppendLine($"- {skill.Name}: {description}");
        }

        return builder.ToString().TrimEnd();
    }
}
