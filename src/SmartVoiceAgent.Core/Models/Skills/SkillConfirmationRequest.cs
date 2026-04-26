namespace SmartVoiceAgent.Core.Models.Skills;

public sealed class SkillConfirmationRequest
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string UserCommand { get; init; } = string.Empty;

    public SkillPlan Plan { get; init; } = new();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string Reason { get; init; } = string.Empty;

    public string SkillId => Plan.SkillId;
}
