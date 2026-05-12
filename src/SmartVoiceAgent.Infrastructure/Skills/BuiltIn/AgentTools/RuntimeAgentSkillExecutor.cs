using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Agent.Tools;

namespace SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

public sealed class RuntimeAgentSkillExecutor : ISkillExecutor
{
    public const string SkillId = "agents.run";
    private const int MaxToolObservationChars = 4000;

    private readonly IRuntimeAgentFactory _runtimeAgentFactory;
    private readonly FileAgentTools? _fileTools;

    public RuntimeAgentSkillExecutor(
        IRuntimeAgentFactory runtimeAgentFactory,
        FileAgentTools? fileTools = null)
    {
        _runtimeAgentFactory = runtimeAgentFactory;
        _fileTools = fileTools;
    }

    public bool CanExecute(string skillId)
    {
        return skillId.Equals(SkillId, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (!CanExecute(plan.SkillId))
        {
            return SkillResult.Failed($"Unsupported agent skill: {plan.SkillId}");
        }

        var task = SkillPlanArgumentReader.GetString(plan, "task");
        if (string.IsNullOrWhiteSpace(task))
        {
            return SkillResult.Failed(
                "Argument 'task' is required.",
                SkillExecutionStatus.ValidationFailed,
                "validation_failed");
        }

        var role = SkillPlanArgumentReader.GetString(plan, "role", "general");
        var agentName = SkillPlanArgumentReader.GetString(plan, "agentName");
        if (string.IsNullOrWhiteSpace(agentName))
        {
            agentName = CreateAgentName(role);
        }

        var observations = await CreateReadOnlyToolContextAsync(role, task, cancellationToken)
            .ConfigureAwait(false);

        var result = await _runtimeAgentFactory
            .RunAsync(new RuntimeAgentRequest(agentName, role, task, observations), cancellationToken)
            .ConfigureAwait(false);

        return SkillResult.Succeeded(result.Response, result);
    }

    private async Task<IReadOnlyList<RuntimeAgentToolObservation>?> CreateReadOnlyToolContextAsync(
        string role,
        string task,
        CancellationToken cancellationToken)
    {
        if (_fileTools is null || !ShouldAttachWorkspaceContext(role, task))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var workspaceMap = await _fileTools
            .DescribeWorkspaceAsync(_fileTools.DefaultWorkingDirectory, maxDepth: 2, maxEntries: 160)
            .ConfigureAwait(false);

        return
        [
            new RuntimeAgentToolObservation(
                "workspace.map",
                Truncate(workspaceMap, MaxToolObservationChars),
                !LooksLikeToolError(workspaceMap))
        ];
    }

    private static bool ShouldAttachWorkspaceContext(string role, string task)
    {
        var value = $"{role} {task}";
        return ContainsAny(
            value,
            "coding",
            "code",
            "repo",
            "repository",
            "workspace",
            "worktree",
            "project",
            "file",
            "diff",
            "test",
            "build",
            "github");
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeToolError(string value)
    {
        return value.StartsWith("Hata:", StringComparison.OrdinalIgnoreCase)
            || value.Contains("hatası:", StringComparison.OrdinalIgnoreCase)
            || value.Contains("error:", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(empty)";
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "...";
    }

    private static string CreateAgentName(string role)
    {
        var normalized = new string(
            role
                .Where(char.IsLetterOrDigit)
                .Take(32)
                .ToArray());

        return string.IsNullOrWhiteSpace(normalized)
            ? "TaskAgent"
            : $"{normalized}Agent";
    }
}
