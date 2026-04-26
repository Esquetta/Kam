using System.Diagnostics;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Execution;

public sealed class SkillExecutionPipeline : ISkillExecutionPipeline
{
    private const int DefaultTimeoutMilliseconds = 30000;

    private readonly ISkillRegistry _registry;
    private readonly IEnumerable<ISkillExecutor> _executors;

    public SkillExecutionPipeline(ISkillRegistry registry, IEnumerable<ISkillExecutor> executors)
    {
        _registry = registry;
        _executors = executors;
    }

    public async Task<SkillResult> ExecuteAsync(SkillPlan plan, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!_registry.TryGet(plan.SkillId, out var manifest) || manifest is null)
        {
            return WithDuration(
                SkillResult.Failed(
                    $"Skill '{plan.SkillId}' is not registered.",
                    SkillExecutionStatus.SkillNotFound,
                    "skill_not_found"),
                stopwatch);
        }

        if (!manifest.Enabled)
        {
            return WithDuration(
                SkillResult.Failed(
                    $"Skill '{plan.SkillId}' is disabled.",
                    SkillExecutionStatus.Disabled,
                    "skill_disabled"),
                stopwatch);
        }

        var validationError = SkillArgumentValidator.Validate(manifest, plan);
        if (validationError is not null)
        {
            return WithDuration(
                SkillResult.Failed(
                    validationError,
                    SkillExecutionStatus.ValidationFailed,
                    "validation_failed"),
                stopwatch);
        }

        var executor = _executors.FirstOrDefault(candidate => candidate.CanExecute(plan.SkillId));
        if (executor is null)
        {
            return WithDuration(
                SkillResult.Failed(
                    $"No executor is registered for skill '{plan.SkillId}'.",
                    SkillExecutionStatus.ExecutorNotFound,
                    "executor_not_found"),
                stopwatch);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeout = GetTimeout(manifest);
        var executionTask = ExecuteCoreAsync(executor, plan, timeoutCts.Token);
        var timeoutTask = Task.Delay(timeout, cancellationToken);

        try
        {
            var completedTask = await Task.WhenAny(executionTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return WithDuration(
                        SkillResult.Failed(
                            $"Skill '{plan.SkillId}' was cancelled.",
                            SkillExecutionStatus.Cancelled,
                            "cancelled"),
                        stopwatch);
                }

                await timeoutCts.CancelAsync();
                _ = ObserveAbandonedTaskAsync(executionTask);
                return WithDuration(
                    SkillResult.Failed(
                        $"Skill '{plan.SkillId}' timed out after {timeout.TotalMilliseconds:0} ms.",
                        SkillExecutionStatus.TimedOut,
                        "timeout"),
                    stopwatch);
            }

            var result = await executionTask;
            return WithDuration(NormalizeResult(plan.SkillId, result), stopwatch);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return WithDuration(
                SkillResult.Failed(
                    $"Skill '{plan.SkillId}' was cancelled.",
                    SkillExecutionStatus.Cancelled,
                    "cancelled"),
                stopwatch);
        }
        catch (Exception ex)
        {
            return WithDuration(
                SkillResult.Failed(
                    $"Skill '{plan.SkillId}' failed: {ex.Message}",
                    SkillExecutionStatus.Failed,
                    "executor_exception"),
                stopwatch);
        }
    }

    private static async Task<SkillResult> ExecuteCoreAsync(
        ISkillExecutor executor,
        SkillPlan plan,
        CancellationToken cancellationToken)
    {
        return await executor.ExecuteAsync(plan, cancellationToken);
    }

    private static TimeSpan GetTimeout(KamSkillManifest manifest)
    {
        var timeoutMilliseconds = manifest.TimeoutMilliseconds > 0
            ? manifest.TimeoutMilliseconds
            : DefaultTimeoutMilliseconds;

        return TimeSpan.FromMilliseconds(timeoutMilliseconds);
    }

    private static SkillResult NormalizeResult(string skillId, SkillResult result)
    {
        if (result.Success)
        {
            return string.IsNullOrWhiteSpace(result.Message)
                ? result with
                {
                    Message = $"Skill {skillId} completed.",
                    Status = SkillExecutionStatus.Succeeded,
                    ErrorCode = string.Empty
                }
                : result with { Status = SkillExecutionStatus.Succeeded, ErrorCode = string.Empty };
        }

        return string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? result with
            {
                ErrorMessage = $"Skill {skillId} failed.",
                Status = result.Status == SkillExecutionStatus.Succeeded
                    ? SkillExecutionStatus.Failed
                    : result.Status
            }
            : result with
            {
                Status = result.Status == SkillExecutionStatus.Succeeded
                    ? SkillExecutionStatus.Failed
                    : result.Status
            };
    }

    private static SkillResult WithDuration(SkillResult result, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return result with { DurationMilliseconds = stopwatch.ElapsedMilliseconds };
    }

    private static async Task ObserveAbandonedTaskAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Result already normalized by timeout path.
        }
    }
}
