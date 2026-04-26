using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.External;

public sealed class ExternalSkillExecutor : ISkillExecutor
{
    private const string LocalExecutorType = "local";
    private const string SkillsShExecutorType = "skills.sh";

    private readonly Func<IChatClient> _chatClientFactory;
    private readonly ISkillRegistry _registry;

    public ExternalSkillExecutor(IChatClient chatClient, ISkillRegistry registry)
        : this(() => chatClient, registry)
    {
    }

    internal ExternalSkillExecutor(Func<IChatClient> chatClientFactory, ISkillRegistry registry)
    {
        _chatClientFactory = chatClientFactory;
        _registry = registry;
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
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, BuildSystemPrompt()),
            new ChatMessage(ChatRole.System, BuildSkillContext(manifest, skillMarkdown, plan)),
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

        return string.IsNullOrWhiteSpace(responseText)
            ? SkillResult.Failed(
                $"Skill '{plan.SkillId}' returned an empty result.",
                SkillExecutionStatus.Failed,
                "empty_external_skill_result")
            : SkillResult.Succeeded(responseText);
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
        Follow the provided SKILL.md instructions for reasoning and response style.
        Do not call tools, APIs, local programs, shell commands, or MCP servers.
        Do not claim that external side effects happened unless the user explicitly provided evidence.
        Return a concise text result that the app can show to the user.
        """;
    }

    private static string BuildSkillContext(
        KamSkillManifest manifest,
        string skillMarkdown,
        SkillPlan plan)
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

        return string.IsNullOrWhiteSpace(directory)
            ? null
            : Path.Combine(directory, "SKILL.md");
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
