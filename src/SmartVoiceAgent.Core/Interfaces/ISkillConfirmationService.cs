using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ISkillConfirmationService
{
    event EventHandler? PendingChanged;

    IReadOnlyCollection<SkillConfirmationRequest> GetPending();

    SkillConfirmationRequest Queue(string userCommand, SkillPlan plan);

    Task<SkillResult> ApproveAsync(Guid requestId, CancellationToken cancellationToken = default);

    bool Reject(Guid requestId);
}
