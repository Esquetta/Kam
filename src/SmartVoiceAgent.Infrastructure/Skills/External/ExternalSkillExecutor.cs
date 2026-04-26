using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Actions;

namespace SmartVoiceAgent.Infrastructure.Skills.External;

public sealed class ExternalSkillExecutor : ISkillExecutor
{
    private const string LocalExecutorType = "local";
    private const string SkillsShExecutorType = "skills.sh";

    private readonly Func<IChatClient> _chatClientFactory;
    private readonly ISkillRegistry _registry;
    private readonly Func<ISkillRuntimeContextProvider?>? _runtimeContextProviderFactory;
    private readonly Func<ISkillActionExecutor?>? _actionExecutorFactory;

    public ExternalSkillExecutor(
        IChatClient chatClient,
        ISkillRegistry registry,
        ISkillRuntimeContextProvider? runtimeContextProvider = null,
        ISkillActionExecutor? actionExecutor = null)
        : this(() => chatClient, registry, runtimeContextProvider, actionExecutor)
    {
    }

    internal ExternalSkillExecutor(
        Func<IChatClient> chatClientFactory,
        ISkillRegistry registry,
        ISkillRuntimeContextProvider? runtimeContextProvider = null,
        ISkillActionExecutor? actionExecutor = null)
        : this(
            chatClientFactory,
            registry,
            runtimeContextProvider is null ? null : () => runtimeContextProvider,
            actionExecutor is null ? null : () => actionExecutor)
    {
    }

    internal ExternalSkillExecutor(
        Func<IChatClient> chatClientFactory,
        ISkillRegistry registry,
        Func<ISkillRuntimeContextProvider?>? runtimeContextProviderFactory,
        Func<ISkillActionExecutor?>? actionExecutorFactory)
    {
        _chatClientFactory = chatClientFactory;
        _registry = registry;
        _runtimeContextProviderFactory = runtimeContextProviderFactory;
        _actionExecutorFactory = actionExecutorFactory;
    }

    public bool CanExecute(string skillId)
    {
        return _registry.TryGet(skillId, out var manifest)
            && manifest is not null
            && IsExternalExecutorType(manifest.ExecutorType);
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGet(plan.SkillId, out var manifest) || manifest is null)
        {
            return SkillResult.Failed(
                $"Skill '{plan.SkillId}' is not registered.",
                SkillExecutionStatus.SkillNotFound,
                "skill_not_found");
        }

        if (!IsExternalExecutorType(manifest.ExecutorType))
        {
            return SkillResult.Failed(
                $"Skill '{plan.SkillId}' is not an external skill.",
                SkillExecutionStatus.ExecutorNotFound,
                "unsupported_executor");
        }

        var skillFile = ResolveSkillFile(manifest);
        if (skillFile is null || !File.Exists(skillFile))
        {
            return SkillResult.Failed(
                $"Skill '{plan.SkillId}' cannot run because SKILL.md was not found.",
                SkillExecutionStatus.ExecutorNotFound,
                "skill_markdown_missing");
        }

        var chatClientResult = TryGetChatClient(plan.SkillId);
        if (!chatClientResult.Success)
        {
            return chatClientResult.Error!;
        }

        var skillMarkdown = await File.ReadAllTextAsync(skillFile, cancellationToken);
        var runtimeContext = await CreateRuntimeContextAsync(plan, cancellationToken);
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, BuildSystemPrompt()),
            new ChatMessage(ChatRole.System, BuildSkillContext(manifest, skillMarkdown, plan, runtimeContext)),
            new ChatMessage(ChatRole.User, BuildUserPrompt(plan))
        };

        var response = await chatClientResult.ChatClient!.GetResponseAsync(
            messages,
            cancellationToken: cancellationToken);
        var responseText = string.Join(
            Environment.NewLine,
            response.Messages
                .Select(message => message.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text)))
            .Trim();

        if (string.IsNullOrWhiteSpace(responseText))
        {
            return SkillResult.Failed(
                $"Skill '{plan.SkillId}' returned an empty result.",
                SkillExecutionStatus.Failed,
                "empty_external_skill_result");
        }

        var parseResult = SkillActionPlanParser.ParseStrict(responseText);
        if (!parseResult.IsValid || parseResult.Plan is null)
        {
            return SkillResult.Failed(
                $"Skill '{plan.SkillId}' returned an invalid action plan: {parseResult.ErrorMessage}",
                SkillExecutionStatus.ValidationFailed,
                "invalid_action_plan");
        }

        var actionPlan = parseResult.Plan;
        if (actionPlan.RequiresConfirmation)
        {
            return SkillResult.Failed(
                "The skill requested user confirmation before action execution.",
                SkillExecutionStatus.ReviewRequired,
                "action_confirmation_required");
        }

        if (actionPlan.Actions.Count == 0)
        {
            return SkillResult.Succeeded(actionPlan.Message);
        }

        var actionExecutor = _actionExecutorFactory?.Invoke();
        if (actionExecutor is null)
        {
            return SkillResult.Failed(
                "The skill returned actions, but the deterministic action executor is unavailable.",
                SkillExecutionStatus.ExecutorNotFound,
                "action_executor_unavailable");
        }

        var actionResult = await actionExecutor.ExecuteAsync(actionPlan, cancellationToken);
        return actionResult.Success
            ? SkillResult.Succeeded(actionResult.Message, actionResult)
            : SkillResult.Failed(
                actionResult.Message,
                SkillExecutionStatus.Failed,
                "action_execution_failed");
    }

    private static bool IsExternalExecutorType(string executorType)
    {
        return executorType.Equals(LocalExecutorType, StringComparison.OrdinalIgnoreCase)
            || executorType.Equals(SkillsShExecutorType, StringComparison.OrdinalIgnoreCase);
    }

    private ChatClientResolution TryGetChatClient(string skillId)
    {
        try
        {
            return ChatClientResolution.Resolved(_chatClientFactory());
        }
        catch (Exception ex)
        {
            return ChatClientResolution.Failed(SkillResult.Failed(
                $"Skill '{skillId}' cannot run because the AI chat client is unavailable: {ex.Message}",
                SkillExecutionStatus.ExecutorNotFound,
                "ai_chat_client_unavailable"));
        }
    }

    private static string BuildSystemPrompt()
    {
        return """
        You are Kam's external skill executor.
        Follow the provided SKILL.md instructions for reasoning, but the application executes actions.
        Do not call tools, APIs, local programs, shell commands, or MCP servers.
        Do not claim that external side effects happened unless the user explicitly provided evidence.
        Return exactly one JSON object and no surrounding text.
        JSON schema:
        {
          "message": "Concise text the app can show to the user.",
          "requiresConfirmation": false,
          "actions": [
            {
              "type": "open_app | focus_window | click | type_text | hotkey | clipboard_set | clipboard_get | read_screen | respond",
              "applicationName": "optional app name",
              "target": "optional window/app/field target",
              "text": "optional text",
              "keys": ["ctrl", "l"],
              "x": 100,
              "y": 200
            }
          ]
        }
        """;
    }

    private static string BuildSkillContext(
        KamSkillManifest manifest,
        string skillMarkdown,
        SkillPlan plan,
        SkillRuntimeContext runtimeContext)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Skill metadata:");
        builder.AppendLine($"- id: {manifest.Id}");
        builder.AppendLine($"- displayName: {manifest.DisplayName}");
        builder.AppendLine($"- source: {manifest.Source}");
        builder.AppendLine($"- checksum: {manifest.Checksum}");
        builder.AppendLine($"- riskLevel: {manifest.RiskLevel}");
        builder.AppendLine();
        builder.AppendLine("Plan arguments JSON:");
        builder.AppendLine(JsonSerializer.Serialize(plan.Arguments));
        builder.AppendLine();
        builder.AppendLine("Runtime context JSON:");
        builder.AppendLine(JsonSerializer.Serialize(runtimeContext));
        builder.AppendLine();
        builder.AppendLine("Allowed action types:");
        builder.AppendLine(string.Join(", ", SkillActionPlanParser.GetSupportedActionTypes()));
        builder.AppendLine();
        builder.AppendLine("SKILL.md:");
        builder.AppendLine(skillMarkdown);
        return builder.ToString();
    }

    private static string BuildUserPrompt(SkillPlan plan)
    {
        if (plan.Arguments.TryGetValue("input", out var input)
            && input.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(input.GetString()))
        {
            return input.GetString()!;
        }

        if (!string.IsNullOrWhiteSpace(plan.Reasoning))
        {
            return plan.Reasoning;
        }

        return JsonSerializer.Serialize(plan.Arguments);
    }

    private static string? ResolveSkillFile(KamSkillManifest manifest)
    {
        var directory = !string.IsNullOrWhiteSpace(manifest.InstalledFrom)
            ? manifest.InstalledFrom
            : ResolveDirectoryFromSource(manifest.Source);

        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        foreach (var fileName in new[] { "SKILL.md", "skill.md", "README.md", "README.txt" })
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, "SKILL.md");
    }

    private static string ResolveDirectoryFromSource(string source)
    {
        foreach (var prefix in new[] { $"{LocalExecutorType}:", $"{SkillsShExecutorType}:" })
        {
            if (source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return source[prefix.Length..];
            }
        }

        return source;
    }

    private async Task<SkillRuntimeContext> CreateRuntimeContextAsync(
        SkillPlan plan,
        CancellationToken cancellationToken)
    {
        var runtimeContextProvider = _runtimeContextProviderFactory?.Invoke();
        if (runtimeContextProvider is not null)
        {
            return await runtimeContextProvider.CreateAsync(plan, cancellationToken);
        }

        return new SkillRuntimeContext
        {
            UserInput = BuildUserPrompt(plan),
            OperatingSystem = Environment.OSVersion.VersionString
        };
    }

    private sealed record ChatClientResolution(
        bool Success,
        IChatClient? ChatClient,
        SkillResult? Error)
    {
        public static ChatClientResolution Resolved(IChatClient chatClient) =>
            new(true, chatClient, null);

        public static ChatClientResolution Failed(SkillResult error) =>
            new(false, null, error);
    }
}
