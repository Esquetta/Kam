using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Agent.Runtime;

/// <summary>
/// Exposes every enabled, runnable skill in the registry as a native tool the model can call.
/// Calls go through <see cref="ISkillExecutionPipeline"/>, so policy, permissions, argument
/// validation, timeouts and history still apply.
/// </summary>
public sealed class SkillToolProvider : IAgentToolProvider
{
    // The agent loop replaces the old single-shot runtime agent, so it is not offered as a tool.
    private static readonly HashSet<string> ExcludedSkillIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "agents.run"
    };

    // Imported SKILL.md skills are instructions, so the agent loads them with load_skill instead of
    // running them through a separate model call.
    private static readonly HashSet<string> InstructionExecutorTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "local",
        "skills.sh"
    };

    private readonly ISkillRegistry _registry;
    private readonly ISkillExecutionPipeline _pipeline;
    private readonly IEnumerable<ISkillExecutor> _executors;

    /// <summary>
    /// Creates the provider.
    /// </summary>
    /// <param name="registry">Skill manifests.</param>
    /// <param name="pipeline">Runs skill calls with policy checks.</param>
    /// <param name="executors">Used to skip skills nothing can run.</param>
    public SkillToolProvider(
        ISkillRegistry registry,
        ISkillExecutionPipeline pipeline,
        IEnumerable<ISkillExecutor> executors)
    {
        _registry = registry;
        _pipeline = pipeline;
        _executors = executors;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentToolDescriptor>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        var executors = _executors.ToList();
        var tools = _registry.GetAll()
            .Where(manifest => manifest.Enabled && !manifest.ReviewRequired)
            .Where(manifest => !ExcludedSkillIds.Contains(manifest.Id))
            .Where(manifest => !InstructionExecutorTypes.Contains(manifest.ExecutorType ?? string.Empty))
            .Where(manifest => executors.Any(executor => executor.CanExecute(manifest.Id)))
            .OrderBy(manifest => manifest.Id, StringComparer.Ordinal)
            .Select(manifest => new AgentToolDescriptor(
                new SkillFunction(manifest, _pipeline),
                ClassifyRisk(manifest),
                string.IsNullOrWhiteSpace(manifest.Source) ? "builtin" : manifest.Source,
                string.IsNullOrWhiteSpace(manifest.DisplayName) ? manifest.Id : manifest.DisplayName))
            .ToList();

        return Task.FromResult<IReadOnlyList<AgentToolDescriptor>>(tools);
    }

    /// <summary>
    /// Converts a skill id such as <c>files.read_lines</c> to a provider-safe tool name (<c>files_read_lines</c>).
    /// </summary>
    /// <param name="skillId">The skill id.</param>
    public static string ToToolName(string skillId)
    {
        var chars = skillId
            .Select(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' ? character : '_')
            .ToArray();
        var name = new string(chars);
        return name.Length <= 64 ? name : name[..64];
    }

    /// <summary>
    /// Maps a skill's risk level and permissions to a tool risk.
    /// </summary>
    /// <param name="manifest">The skill manifest.</param>
    public static ToolRisk ClassifyRisk(KamSkillManifest manifest)
    {
        var permissions = manifest.Permissions;

        if (manifest.Id.StartsWith("communication.", StringComparison.OrdinalIgnoreCase)
            && manifest.RiskLevel == SkillRiskLevel.High)
        {
            return ToolRisk.External;
        }

        if (permissions.Contains(SkillPermission.ProcessLaunch)
            || permissions.Contains(SkillPermission.ProcessControl)
            || manifest.Id.Equals("shell.run", StringComparison.OrdinalIgnoreCase))
        {
            return ToolRisk.Execute;
        }

        if (permissions.Contains(SkillPermission.FileSystemWrite)
            || permissions.Contains(SkillPermission.ClipboardWrite))
        {
            return ToolRisk.Write;
        }

        if (manifest.RiskLevel == SkillRiskLevel.High)
        {
            return ToolRisk.Execute;
        }

        return permissions.Contains(SkillPermission.Network) ? ToolRisk.Network : ToolRisk.Read;
    }

    /// <summary>
    /// Builds the JSON schema for a skill's arguments.
    /// </summary>
    /// <param name="manifest">The skill manifest.</param>
    public static JsonElement BuildSchema(KamSkillManifest manifest)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var argument in manifest.Arguments)
        {
            var property = new JsonObject();
            switch (argument.Type)
            {
                case SkillArgumentType.String:
                    property["type"] = "string";
                    break;
                case SkillArgumentType.Number:
                    property["type"] = "number";
                    break;
                case SkillArgumentType.Boolean:
                    property["type"] = "boolean";
                    break;
                case SkillArgumentType.Object:
                    property["type"] = "object";
                    break;
                case SkillArgumentType.Array:
                    property["type"] = "array";
                    property["items"] = new JsonObject();
                    break;
            }

            if (!string.IsNullOrWhiteSpace(argument.Description))
            {
                property["description"] = argument.Description;
            }

            properties[argument.Name] = property;
            if (argument.Required)
            {
                required.Add(argument.Name);
            }
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return JsonSerializer.SerializeToElement(schema);
    }

    private static string BuildDescription(KamSkillManifest manifest)
    {
        var title = string.IsNullOrWhiteSpace(manifest.DisplayName) ? manifest.Id : manifest.DisplayName;
        return string.IsNullOrWhiteSpace(manifest.Description)
            ? $"{title}."
            : $"{title}. {manifest.Description.Trim()}";
    }

    private sealed class SkillFunction : AIFunction
    {
        private readonly KamSkillManifest _manifest;
        private readonly ISkillExecutionPipeline _pipeline;
        private readonly JsonElement _schema;
        private readonly string _name;
        private readonly string _description;

        public SkillFunction(KamSkillManifest manifest, ISkillExecutionPipeline pipeline)
        {
            _manifest = manifest;
            _pipeline = pipeline;
            _schema = BuildSchema(manifest);
            _name = ToToolName(manifest.Id);
            _description = BuildDescription(manifest);
        }

        public override string Name => _name;

        public override string Description => _description;

        public override JsonElement JsonSchema => _schema;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            var plan = new SkillPlan
            {
                SkillId = _manifest.Id,
                Confidence = 1,
                Reasoning = "agent tool call",
                // The agent runtime has already applied the approval policy for this exact call.
                IsConfirmedByUser = true
            };

            foreach (var (key, value) in arguments)
            {
                plan.Arguments[key] = value switch
                {
                    JsonElement element => element.Clone(),
                    null => JsonSerializer.SerializeToElement<object?>(null),
                    _ => JsonSerializer.SerializeToElement(value, value.GetType())
                };
            }

            var result = await _pipeline.ExecuteAsync(plan, cancellationToken);
            return FormatResult(result);
        }

        private static string FormatResult(SkillResult result)
        {
            if (!result.Success)
            {
                return $"Error ({result.Status}): {result.ErrorMessage}";
            }

            if (result.Data is null)
            {
                return string.IsNullOrWhiteSpace(result.Message) ? "Done." : result.Message;
            }

            string data;
            try
            {
                data = result.Data as string ?? JsonSerializer.Serialize(result.Data, result.Data.GetType());
            }
            catch (NotSupportedException)
            {
                data = result.Data.ToString() ?? string.Empty;
            }

            return string.IsNullOrWhiteSpace(result.Message) ? data : $"{result.Message}\n{data}";
        }
    }
}
