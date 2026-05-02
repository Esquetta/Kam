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
                "apps.check checks an application",
                "apps.check",
                new { applicationName = "notepad" }),
            Case(
                "apps.path resolves an application path",
                "apps.path",
                new { applicationName = "notepad" }),
            Case(
                "apps.running checks process state",
                "apps.running",
                new { applicationName = "notepad" }),
            Case(
                "apps.installed.list returns installed applications",
                "apps.installed.list",
                new { includeSystemApps = false }),
            Case(
                "system.device.control reads volume status",
                "system.device.control",
                new { deviceName = "volume", action = "status" }),
            Case(
                "system.info reads system diagnostics",
                "system.info",
                new { }),
            Case(
                "system.cpu reads CPU diagnostics",
                "system.cpu",
                new { }),
            Case(
                "system.memory reads memory diagnostics",
                "system.memory",
                new { }),
            Case(
                "system.disk reads disk diagnostics",
                "system.disk",
                new { }),
            Case(
                "system.battery reads battery diagnostics",
                "system.battery",
                new { }),
            Case(
                "system.processes.list reads bounded process diagnostics",
                "system.processes.list",
                new { sortBy = "memory", count = 5 }),
            Case(
                "files.read reads a stable source file",
                "files.read",
                new { filePath = GetStableReadableFilePath() }),
            Case(
                "files.exists checks a stable user path",
                "files.exists",
                new { filePath = GetStableUserPath() }),
            Case(
                "files.info inspects a stable source file",
                "files.info",
                new { filePath = GetStableReadableFilePath() }),
            Case(
                "files.list lists a stable user path",
                "files.list",
                new { directoryPath = GetStableUserPath(), searchPattern = "*.*", recursive = false, openFolder = false }),
            Case(
                "files.search searches a stable user path",
                "files.search",
                new { directoryPath = GetStableUserPath(), searchPattern = "*.md", recursive = false }),
            Case(
                "files.search_content performs bounded text search",
                "files.search_content",
                new
                {
                    directoryPath = GetStableUserPath(),
                    query = "kam-skill-smoke-noop",
                    searchPattern = "*.md",
                    recursive = false,
                    maxMatches = 5
                }),
            Case(
                "files.tree inspects a stable user path",
                "files.tree",
                new { directoryPath = GetStableUserPath(), maxDepth = 1, maxEntries = 50 }),
            Case(
                "files.read_lines reads bounded source lines",
                "files.read_lines",
                new { filePath = GetStableReadableFilePath(), startLine = 1, lineCount = 5 }),
            Case(
                "file.read reads a stable source file",
                "file.read",
                new { filePath = GetStableReadableFilePath() }),
            Case(
                "file.read_range reads bounded source lines",
                "file.read_range",
                new { filePath = GetStableReadableFilePath(), startLine = 1, lineCount = 5 }),
            Case(
                "workspace.tree inspects a stable user path",
                "workspace.tree",
                new { directoryPath = GetStableUserPath(), maxDepth = 1, maxEntries = 50 }),
            Case(
                "workspace.find_files searches a stable user path",
                "workspace.find_files",
                new { directoryPath = GetStableUserPath(), searchPattern = "*.md", recursive = false }),
            Case(
                "workspace.search_text performs bounded text search",
                "workspace.search_text",
                new
                {
                    directoryPath = GetStableUserPath(),
                    query = "kam-skill-smoke-noop",
                    searchPattern = "*.md",
                    recursive = false,
                    maxMatches = 5
                }),
            Case(
                "workspace.map inspects a stable user path",
                "workspace.map",
                new { directoryPath = GetStableUserPath(), maxDepth = 1, maxEntries = 50 }),
            Case(
                "code.search performs bounded text search",
                "code.search",
                new
                {
                    directoryPath = GetStableUserPath(),
                    query = "kam-skill-smoke-noop",
                    searchPattern = "*.md",
                    recursive = false,
                    maxMatches = 5
                }),
            Case(
                "code.outline reads a stable source outline",
                "code.outline",
                new { filePath = GetStableCodeFilePath(), maxSymbols = 20 }),
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
                new { query = "Kam voice automation", lang = "en", results = 1 }),
            Case(
                "window.active reads active window context",
                "window.active",
                new { }),
            Case(
                "window.list reads bounded visible windows",
                "window.list",
                new { maxWindows = 5 }),
            Case(
                "accessibility.tree reads bounded screen context",
                "accessibility.tree",
                new { maxScreens = 1, maxNodes = 20, includeObjects = false }),
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

    private static string GetStableReadableFilePath()
    {
        var codeFile = GetStableCodeFilePath();
        if (File.Exists(codeFile))
        {
            return codeFile;
        }

        var assemblyPath = typeof(BuiltInSkillEvalCaseCatalog).Assembly.Location;
        if (File.Exists(assemblyPath))
        {
            return assemblyPath;
        }

        return Environment.ProcessPath ?? Environment.CurrentDirectory;
    }

    private static string GetStableCodeFilePath()
    {
        var repositoryRoot = FindRepositoryRoot();
        if (!string.IsNullOrWhiteSpace(repositoryRoot))
        {
            var sourcePath = Path.Combine(
                repositoryRoot,
                "src",
                "SmartVoiceAgent.Infrastructure",
                "Skills",
                "Evaluation",
                "BuiltInSkillEvalCaseCatalog.cs");
            if (File.Exists(sourcePath))
            {
                return sourcePath;
            }
        }

        var currentPath = Path.Combine(
            Environment.CurrentDirectory,
            "src",
            "SmartVoiceAgent.Infrastructure",
            "Skills",
            "Evaluation",
            "BuiltInSkillEvalCaseCatalog.cs");
        if (File.Exists(currentPath))
        {
            return currentPath;
        }

        return typeof(BuiltInSkillEvalCaseCatalog).Assembly.Location;
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
