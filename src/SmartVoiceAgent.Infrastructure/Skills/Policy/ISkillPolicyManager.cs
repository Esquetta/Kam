namespace SmartVoiceAgent.Infrastructure.Skills.Policy;

public interface ISkillPolicyManager
{
    Task<bool> ApproveReviewAsync(string skillId, CancellationToken cancellationToken = default);

    Task<bool> EnableAsync(string skillId, CancellationToken cancellationToken = default);

    Task<bool> DisableAsync(string skillId, CancellationToken cancellationToken = default);

    Task<bool> RevokePermissionsAsync(string skillId, CancellationToken cancellationToken = default);

    Task<bool> GrantPermissionsAsync(string skillId, CancellationToken cancellationToken = default);
}
