using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Services;

public sealed class SkillConfirmationService : ISkillConfirmationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SkillConfirmationService> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, SkillConfirmationRequest> _pending = [];

    public SkillConfirmationService(
        IServiceScopeFactory scopeFactory,
        ILogger<SkillConfirmationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public event EventHandler? PendingChanged;

    public IReadOnlyCollection<SkillConfirmationRequest> GetPending()
    {
        lock (_gate)
        {
            return _pending.Values
                .OrderBy(request => request.CreatedAt)
                .ToArray();
        }
    }

    public SkillConfirmationRequest Queue(
        string userCommand,
        SkillPlan plan,
        string? reason = null,
        string? preview = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var request = new SkillConfirmationRequest
        {
            Id = Guid.NewGuid(),
            UserCommand = userCommand,
            Plan = plan,
            CreatedAt = DateTimeOffset.UtcNow,
            Reason = !string.IsNullOrWhiteSpace(reason)
                ? reason
                : string.IsNullOrWhiteSpace(plan.Reasoning)
                ? "This skill requires confirmation before execution."
                : plan.Reasoning,
            Preview = preview ?? string.Empty
        };

        lock (_gate)
        {
            _pending[request.Id] = request;
        }

        OnPendingChanged();
        return request;
    }

    public async Task<SkillResult> ApproveAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        SkillConfirmationRequest request;

        lock (_gate)
        {
            if (!_pending.Remove(requestId, out request!))
            {
                return SkillResult.Failed(
                    "Confirmation request was not found.",
                    SkillExecutionStatus.ValidationFailed,
                    "confirmation_not_found");
            }
        }

        OnPendingChanged();

        try
        {
            request.Plan.IsConfirmedByUser = true;
            using var scope = _scopeFactory.CreateScope();
            var pipeline = scope.ServiceProvider.GetRequiredService<ISkillExecutionPipeline>();
            return await pipeline.ExecuteAsync(request.Plan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Confirmed skill execution failed for {SkillId}.", request.SkillId);
            return SkillResult.Failed(
                $"Confirmed skill execution failed: {ex.Message}",
                SkillExecutionStatus.Failed,
                "confirmation_execution_failed");
        }
    }

    public bool Reject(Guid requestId)
    {
        bool removed;
        lock (_gate)
        {
            removed = _pending.Remove(requestId);
        }

        if (removed)
        {
            OnPendingChanged();
        }

        return removed;
    }

    private void OnPendingChanged()
    {
        PendingChanged?.Invoke(this, EventArgs.Empty);
    }
}
