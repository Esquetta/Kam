namespace SmartVoiceAgent.Infrastructure.Skills.Policy;

public static class SkillRuntimePolicyOptions
{
    public const string ShellBlockedPatterns = "shell.blockedPatterns";
    public const string ShellAllowedCommands = "shell.allowedCommands";
    public const string ShellAllowedWorkingDirectories = "shell.allowedWorkingDirectories";
    public const string WebAllowedHosts = "web.allowedHosts";
    public const string WebBlockedHosts = "web.blockedHosts";
    public const string WebAllowPrivateNetwork = "web.allowPrivateNetwork";
    public const string SmokeSkipReason = "smoke.skipReason";

    private static readonly char[] ListSeparators = [';', ',', '\r', '\n'];

    public static bool IsEditableSkill(string skillId)
    {
        return skillId.Equals("shell.run", StringComparison.OrdinalIgnoreCase)
            || skillId.Equals("web.fetch", StringComparison.OrdinalIgnoreCase)
            || skillId.Equals("web.read_page", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetDefaultOptionKey(string skillId)
    {
        if (skillId.Equals("shell.run", StringComparison.OrdinalIgnoreCase))
        {
            return ShellBlockedPatterns;
        }

        if (skillId.Equals("web.fetch", StringComparison.OrdinalIgnoreCase)
            || skillId.Equals("web.read_page", StringComparison.OrdinalIgnoreCase))
        {
            return WebAllowedHosts;
        }

        return string.Empty;
    }

    public static string Describe(string skillId, IReadOnlyDictionary<string, string> runtimeOptions)
    {
        var smokeCoverageText = runtimeOptions.TryGetValue(SmokeSkipReason, out var smokeSkipReason)
            && !string.IsNullOrWhiteSpace(smokeSkipReason)
            ? $"Smoke coverage: not applicable - {smokeSkipReason.Trim()}"
            : string.Empty;

        if (!IsEditableSkill(skillId))
        {
            return string.IsNullOrWhiteSpace(smokeCoverageText)
                ? "Runtime policy: not configurable for this skill."
                : smokeCoverageText;
        }

        var options = runtimeOptions
            .Where(option => !string.IsNullOrWhiteSpace(option.Key)
                && !string.IsNullOrWhiteSpace(option.Value))
            .Where(option => !option.Key.Equals(SmokeSkipReason, StringComparison.OrdinalIgnoreCase))
            .OrderBy(option => option.Key, StringComparer.OrdinalIgnoreCase)
            .Select(option => $"{option.Key}={option.Value}")
            .ToArray();

        if (options.Length > 0)
        {
            var runtimeText = $"Runtime policy: {string.Join(" | ", options)}";
            return string.IsNullOrWhiteSpace(smokeCoverageText)
                ? runtimeText
                : $"{runtimeText} | {smokeCoverageText}";
        }

        var defaultText = skillId.Equals("shell.run", StringComparison.OrdinalIgnoreCase)
            ? $"Runtime policy: set {ShellBlockedPatterns}, {ShellAllowedCommands}, or {ShellAllowedWorkingDirectories}."
            : $"Runtime policy: set {WebAllowedHosts}, {WebBlockedHosts}, or {WebAllowPrivateNetwork}.";

        return string.IsNullOrWhiteSpace(smokeCoverageText)
            ? defaultText
            : $"{defaultText} | {smokeCoverageText}";
    }

    public static IReadOnlyCollection<string> SplitList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
