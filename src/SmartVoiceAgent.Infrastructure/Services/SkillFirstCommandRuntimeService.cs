using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Commands;
using SmartVoiceAgent.Core.Models.Skills;

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

        try
        {
            var pipeline = scope.ServiceProvider.GetRequiredService<ISkillExecutionPipeline>();
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

    private static bool RequiresConfirmation(SkillPlan plan, ISkillRegistry registry)
    {
        if (plan.RequiresConfirmation)
        {
            return true;
        }

        return registry.TryGet(plan.SkillId, out var manifest)
            && manifest?.RiskLevel == SkillRiskLevel.High;
    }

    private static bool IsActionConfirmationRequired(SkillResult result)
    {
        return !result.Success
            && result.Status == SkillExecutionStatus.ReviewRequired
            && result.ErrorCode.Equals("action_confirmation_required", StringComparison.OrdinalIgnoreCase);
    }
}
