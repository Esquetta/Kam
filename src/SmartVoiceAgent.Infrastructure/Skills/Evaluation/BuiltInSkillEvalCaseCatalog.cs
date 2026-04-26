using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Evaluation;

public sealed class BuiltInSkillEvalCaseCatalog : ISkillEvalCaseCatalog
{
    public IReadOnlyCollection<SkillEvalCase> CreateSmokeCases()
    {
        return
        [
            Case(
                "apps.list returns installed applications",
                "apps.list",
                new { }),
            Case(
                "apps.status checks an application",
                "apps.status",
                new { applicationName = "notepad" }),
            Case(
                "system.device.control reads volume status",
                "system.device.control",
                new { deviceName = "volume", action = "status" }),
            Case(
                "system.info reads system diagnostics",
                "system.info",
                new { }),
            Case(
                "files.exists checks a stable user path",
                "files.exists",
                new { filePath = GetStableUserPath() }),
            Case(
                "files.tree inspects a stable user path",
                "files.tree",
                new { directoryPath = GetStableUserPath(), maxDepth = 1, maxEntries = 50 }),
            Case(
                "clipboard.get reads bounded clipboard text",
                "clipboard.get",
                new { maxLength = 256 }),
            Case(
                "clipboard.peek reads bounded clipboard preview",
                "clipboard.peek",
                new { maxLength = 256 }),
            Case(
                "shell.run executes a bounded non-interactive command",
                "shell.run",
                new { command = "echo kam-skill-smoke", timeoutMilliseconds = 5000, maxOutputLength = 1000 }),
            Case(
                "web.search creates a bounded search plan",
                "web.search",
                new { query = "Kam voice automation", lang = "en", results = 3 }),
            Case(
                "communication.email.validate validates an address",
                "communication.email.validate",
                new { email = "test@example.com" }),
            Case(
                "communication.sms.validate validates a phone number",
                "communication.sms.validate",
                new { phoneNumber = "+15551234567" })
        ];
    }

    private static SkillEvalCase Case(string name, string skillId, object arguments)
    {
        return new SkillEvalCase
        {
            Name = name,
            Plan = SkillPlan.FromObject(skillId, arguments),
            ExpectedStatus = SkillExecutionStatus.Succeeded
        };
    }

    private static string GetStableUserPath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userProfile)
            ? Environment.CurrentDirectory
            : userProfile;
    }
}
