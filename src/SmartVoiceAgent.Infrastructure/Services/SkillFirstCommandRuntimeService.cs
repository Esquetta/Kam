using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Commands;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Execution;
using System.Text.Json;

namespace SmartVoiceAgent.Infrastructure.Services;

public sealed class SkillFirstCommandRuntimeService : ICommandRuntimeService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SkillFirstCommandRuntimeService> _logger;

    public SkillFirstCommandRuntimeService(
        IServiceScopeFactory scopeFactory,
        ILogger<SkillFirstCommandRuntimeService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<CommandRuntimeResult> ExecuteAsync(
        string command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return CommandRuntimeResult.Failed(
                "Command is required.",
                SkillExecutionStatus.ValidationFailed,
                "command_required");
        }

        using var scope = _scopeFactory.CreateScope();
        var planner = scope.ServiceProvider.GetRequiredService<ISkillPlannerService>();
        var confirmationService = scope.ServiceProvider.GetRequiredService<ISkillConfirmationService>();
        var skillRegistry = scope.ServiceProvider.GetRequiredService<ISkillRegistry>();

        var planResult = await CreatePlanAsync(planner, command, cancellationToken);
        if (!planResult.IsValid || planResult.Plan is null)
        {
            return CommandRuntimeResult.Failed(
                $"Could not create skill plan: {planResult.ErrorMessage}",
                SkillExecutionStatus.ValidationFailed,
                "planner_invalid");
        }

        var plan = planResult.Plan;
        var planValidationError = ValidatePlan(plan, skillRegistry);
        if (!string.IsNullOrWhiteSpace(planValidationError))
        {
            return CommandRuntimeResult.Failed(
                $"Could not create skill plan: {planValidationError}",
                SkillExecutionStatus.ValidationFailed,
                "planner_invalid",
                plan.SkillId);
        }

        try
        {
            var pipeline = scope.ServiceProvider.GetRequiredService<ISkillExecutionPipeline>();

            if (RequiresPreviewBeforeConfirmation(plan, skillRegistry))
            {
                return await PreviewAndQueueFileEditAsync(
                    command,
                    plan,
                    pipeline,
                    confirmationService,
                    cancellationToken);
            }

            if (RequiresConfirmation(plan, skillRegistry))
            {
                var request = confirmationService.Queue(
                    command,
                    plan,
                    $"Skill '{plan.SkillId}' requires confirmation before execution.");
                return CommandRuntimeResult.PendingConfirmation(
                    $"Skill '{plan.SkillId}' requires confirmation before execution.",
                    plan.SkillId,
                    request.Id);
            }

            var result = await pipeline.ExecuteAsync(plan, cancellationToken);
            if (IsActionConfirmationRequired(result))
            {
                var request = confirmationService.Queue(command, plan, result.ErrorMessage);
                return CommandRuntimeResult.PendingConfirmation(
                    result.ErrorMessage,
                    plan.SkillId,
                    request.Id);
            }

            return result.Success
                ? CommandRuntimeResult.Succeeded(result.Message, plan.SkillId, result)
                : CommandRuntimeResult.Failed(
                    result.ErrorMessage,
                    result.Status,
                    string.IsNullOrWhiteSpace(result.ErrorCode) ? "skill_failed" : result.ErrorCode,
                    plan.SkillId,
                    result.DurationMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Skill runtime failed while executing command.");
            return CommandRuntimeResult.Failed(
                $"Skill runtime failed: {ex.Message}",
                SkillExecutionStatus.Failed,
                "runtime_exception",
                plan.SkillId);
        }
    }

    private static string? ValidatePlan(SkillPlan plan, ISkillRegistry registry)
    {
        if (!registry.TryGet(plan.SkillId, out var manifest) || manifest is null)
        {
            return $"Planner returned unknown skill '{plan.SkillId}'.";
        }

        return SkillArgumentValidator.Validate(manifest, plan);
    }

    private async Task<SkillPlanParseResult> CreatePlanAsync(
        ISkillPlannerService planner,
        string command,
        CancellationToken cancellationToken)
    {
        try
        {
            return await planner.CreatePlanAsync(command, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Skill planner failed while creating a plan.");
            return SkillPlanParseResult.Failure(ex.Message);
        }
    }

    private static async Task<CommandRuntimeResult> PreviewAndQueueFileEditAsync(
        string command,
        SkillPlan plan,
        ISkillExecutionPipeline pipeline,
        ISkillConfirmationService confirmationService,
        CancellationToken cancellationToken)
    {
        var previewPlan = CreatePreviewPlan(plan);
        var previewResult = await pipeline.ExecuteAsync(previewPlan, cancellationToken);
        if (!previewResult.Success)
        {
            return CommandRuntimeResult.Failed(
                previewResult.ErrorMessage,
                previewResult.Status,
                string.IsNullOrWhiteSpace(previewResult.ErrorCode)
                    ? "preview_failed"
                    : previewResult.ErrorCode,
                plan.SkillId,
                previewResult.DurationMilliseconds);
        }

        var request = confirmationService.Queue(
            command,
            plan,
            $"Review the diff for skill '{plan.SkillId}' before applying changes.",
            previewResult.Message);

        return CommandRuntimeResult.PendingConfirmation(
            $"Skill '{plan.SkillId}' preview is ready for confirmation.",
            plan.SkillId,
            request.Id);
    }

    private static SkillPlan CreatePreviewPlan(SkillPlan plan)
    {
        var arguments = plan.Arguments.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone());
        arguments["previewOnly"] = JsonSerializer.SerializeToElement(true).Clone();

        return new SkillPlan
        {
            SkillId = plan.SkillId,
            Arguments = arguments,
            Confidence = plan.Confidence,
            RequiresConfirmation = false,
            Reasoning = plan.Reasoning
        };
    }

    private static bool RequiresPreviewBeforeConfirmation(SkillPlan plan, ISkillRegistry registry)
    {
        return IsPreviewableFileEditSkill(plan.SkillId)
            && !IsPreviewOnlyFileEditPlan(plan)
            && RequiresConfirmation(plan, registry);
    }

    private static bool RequiresConfirmation(SkillPlan plan, ISkillRegistry registry)
    {
        if (plan.IsConfirmedByUser || IsPreviewOnlyFileEditPlan(plan))
        {
            return false;
        }

        if (plan.RequiresConfirmation)
        {
            return true;
        }

        return registry.TryGet(plan.SkillId, out var manifest)
            && manifest?.RiskLevel == SkillRiskLevel.High;
    }

    private static bool IsPreviewableFileEditSkill(string skillId)
    {
        return skillId.Equals("file.patch", StringComparison.OrdinalIgnoreCase)
            || skillId.Equals("file.replace_range", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPreviewOnlyFileEditPlan(SkillPlan plan)
    {
        return IsPreviewableFileEditSkill(plan.SkillId)
            && plan.Arguments.TryGetValue("previewOnly", out var previewOnly)
            && previewOnly.ValueKind == JsonValueKind.True;
    }

    private static bool IsActionConfirmationRequired(SkillResult result)
    {
        return !result.Success
            && result.Status == SkillExecutionStatus.ReviewRequired
            && result.ErrorCode.Equals("action_confirmation_required", StringComparison.OrdinalIgnoreCase);
    }
}
