using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

public static class AgentToolSkillResult
{
    public static SkillResult FromMessage(string message)
    {
        return LooksLikeFailure(message)
            ? SkillResult.Failed(message)
            : SkillResult.Succeeded(message);
    }

    private static bool LooksLikeFailure(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = message.TrimStart();
        if (normalized.StartsWith("📋 Clipboard content:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Clipboard content:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return normalized.StartsWith("Hata", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Failed", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Cannot ", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(" hata", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(" cannot ", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("error", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("not configured", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("not supported", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("reddedildi", StringComparison.OrdinalIgnoreCase);
    }
}
