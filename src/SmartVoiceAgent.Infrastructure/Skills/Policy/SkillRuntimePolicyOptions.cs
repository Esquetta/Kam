namespace SmartVoiceAgent.Infrastructure.Skills.Policy;

public static class SkillRuntimePolicyOptions
{
    public const string ShellBlockedPatterns = "shell.blockedPatterns";
    public const string ShellAllowedCommands = "shell.allowedCommands";
    public const string ShellAllowedWorkingDirectories = "shell.allowedWorkingDirectories";
    public const string WebAllowedHosts = "web.allowedHosts";
    public const string WebBlockedHosts = "web.blockedHosts";
    public const string WebAllowPrivateNetwork = "web.allowPrivateNetwork";

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
        if (!IsEditableSkill(skillId))
        {
            return "Runtime policy: not configurable for this skill.";
        }

        var options = runtimeOptions
            .Where(option => !string.IsNullOrWhiteSpace(option.Key)
                && !string.IsNullOrWhiteSpace(option.Value))
            .OrderBy(option => option.Key, StringComparer.OrdinalIgnoreCase)
            .Select(option => $"{option.Key}={option.Value}")
            .ToArray();

        if (options.Length > 0)
        {
            return $"Runtime policy: {string.Join(" | ", options)}";
        }

        return skillId.Equals("shell.run", StringComparison.OrdinalIgnoreCase)
            ? $"Runtime policy: set {ShellBlockedPatterns}, {ShellAllowedCommands}, or {ShellAllowedWorkingDirectories}."
            : $"Runtime policy: set {WebAllowedHosts}, {WebBlockedHosts}, or {WebAllowPrivateNetwork}.";
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
