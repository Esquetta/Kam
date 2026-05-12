namespace SmartVoiceAgent.Core.Models.Agents;

public sealed record RuntimeAgentToolObservation(
    string SkillId,
    string Summary,
    bool Success);
