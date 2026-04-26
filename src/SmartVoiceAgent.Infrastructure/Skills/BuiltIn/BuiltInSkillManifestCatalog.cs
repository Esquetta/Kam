using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.BuiltIn;

public static class BuiltInSkillManifestCatalog
{
    public static IReadOnlyCollection<KamSkillManifest> CreateAll()
    {
        return
        [
            Create(
                "apps.open",
                "Open Application",
                SkillRiskLevel.High,
                [SkillPermission.ProcessLaunch],
                [RequiredString("applicationName", "Application display name or executable alias.")],
                timeoutMilliseconds: 10000),
            Create(
                "apps.close",
                "Close Application",
                SkillRiskLevel.High,
                [SkillPermission.ProcessControl],
                [RequiredString("applicationName", "Application display name or executable alias.")],
                timeoutMilliseconds: 10000),
            Create(
                "apps.status",
                "Application Status",
                SkillRiskLevel.Low,
                [SkillPermission.None],
                [RequiredString("applicationName", "Application display name or executable alias.")],
                timeoutMilliseconds: 5000),
            Create(
                "apps.list",
                "List Applications",
                SkillRiskLevel.Low,
                [SkillPermission.None],
                timeoutMilliseconds: 5000),
            Create(
                "apps.check",
                "Check Application",
                SkillRiskLevel.Low,
                [SkillPermission.None],
                [RequiredString("applicationName", "Application display name or executable alias.")]),
            Create(
                "apps.path",
                "Get Application Path",
                SkillRiskLevel.Low,
                [SkillPermission.FileSystemRead],
                [RequiredString("applicationName", "Application display name or executable alias.")]),
            Create(
                "apps.running",
                "Check Application Running",
                SkillRiskLevel.Low,
                [SkillPermission.None],
                [RequiredString("applicationName", "Application display name or executable alias.")]),
            Create(
                "apps.installed.list",
                "List Installed Applications",
                SkillRiskLevel.Low,
                [SkillPermission.FileSystemRead],
                [OptionalBool("includeSystemApps", "Include system applications.")],
                timeoutMilliseconds: 15000),
            Create(
                "media.play",
                "Play Music",
                SkillRiskLevel.Medium,
                [SkillPermission.ProcessLaunch],
                [RequiredString("trackName", "Track, playlist, or search query to play.")],
                timeoutMilliseconds: 15000),
            Create(
                "system.device.control",
                "Control Device",
                SkillRiskLevel.High,
                [SkillPermission.ProcessControl],
                [
                    RequiredString("deviceName", "Device name such as volume, wifi, bluetooth, brightness, or power."),
                    RequiredString("action", "Action such as increase, decrease, on, off, toggle, status, or shutdown.")
                ]),
            Create(
                "system.info",
                "System Information",
                SkillRiskLevel.Low,
                [SkillPermission.SystemInformation],
                timeoutMilliseconds: 10000),
            Create(
                "system.cpu",
                "CPU Information",
                SkillRiskLevel.Low,
                [SkillPermission.SystemInformation],
                timeoutMilliseconds: 5000),
            Create(
                "system.memory",
                "Memory Information",
                SkillRiskLevel.Low,
                [SkillPermission.SystemInformation],
                timeoutMilliseconds: 5000),
            Create(
                "system.disk",
                "Disk Information",
                SkillRiskLevel.Low,
                [SkillPermission.SystemInformation],
                timeoutMilliseconds: 5000),
            Create(
                "system.battery",
                "Battery Status",
                SkillRiskLevel.Low,
                [SkillPermission.SystemInformation],
                timeoutMilliseconds: 5000),
            Create(
                "system.processes.list",
                "List Processes",
                SkillRiskLevel.Low,
                [SkillPermission.SystemInformation],
                [
                    OptionalString("sortBy", "Sort by cpu or memory."),
                    OptionalNumber("count", "Number of processes to return.")
                ],
                timeoutMilliseconds: 5000),
            Create(
                "system.process.kill",
                "Kill Process",
                SkillRiskLevel.High,
                [SkillPermission.ProcessControl],
                [
                    RequiredString("processNameOrId", "Process name or PID to terminate."),
                    OptionalBool("force", "Force kill immediately.")
                ],
                timeoutMilliseconds: 10000),
            Create(
                "files.read",
                "Read File",
                SkillRiskLevel.Low,
                [SkillPermission.FileSystemRead],
                [RequiredString("filePath", "Full path to the file.")]),
            Create(
                "files.write",
                "Write File",
                SkillRiskLevel.High,
                [SkillPermission.FileSystemWrite],
                [
                    RequiredString("filePath", "Full path to the file."),
                    RequiredString("content", "Content to write."),
                    OptionalBool("append", "Append instead of overwrite."),
                    OptionalBool("openAfterWrite", "Open the file after writing.")
                ]),
            Create(
                "files.create",
                "Create File",
                SkillRiskLevel.High,
                [SkillPermission.FileSystemWrite],
                [
                    RequiredString("filePath", "Full path to the file."),
                    OptionalString("content", "Initial content."),
                    OptionalBool("openAfterCreation", "Open the file after creation.")
                ]),
            Create(
                "files.delete",
                "Delete File",
                SkillRiskLevel.High,
                [SkillPermission.FileSystemWrite],
                [RequiredString("filePath", "Full path to the file.")]),
            Create(
                "files.copy",
                "Copy File",
                SkillRiskLevel.Medium,
                [SkillPermission.FileSystemRead, SkillPermission.FileSystemWrite],
                [
                    RequiredString("sourcePath", "Source file path."),
                    RequiredString("destinationPath", "Destination file path."),
                    OptionalBool("overwrite", "Overwrite destination file."),
                    OptionalBool("showInFolder", "Show destination in Explorer.")
                ]),
            Create(
                "files.move",
                "Move File",
                SkillRiskLevel.High,
                [SkillPermission.FileSystemRead, SkillPermission.FileSystemWrite],
                [
                    RequiredString("sourcePath", "Source file path."),
                    RequiredString("destinationPath", "Destination file path."),
                    OptionalBool("overwrite", "Overwrite destination file.")
                ]),
            Create(
                "files.exists",
                "File Exists",
                SkillRiskLevel.Low,
                [SkillPermission.FileSystemRead],
                [RequiredString("filePath", "Full path to check.")]),
            Create(
                "files.info",
                "Get File Info",
                SkillRiskLevel.Low,
                [SkillPermission.FileSystemRead],
                [RequiredString("filePath", "Full path to the file.")]),
            Create(
                "files.list",
                "List Files",
                SkillRiskLevel.Low,
                [SkillPermission.FileSystemRead],
                [
                    RequiredString("directoryPath", "Directory path to list."),
                    OptionalString("searchPattern", "Search pattern such as *.txt."),
                    OptionalBool("recursive", "Search subdirectories."),
                    OptionalBool("openFolder", "Open folder after listing.")
                ]),
            Create(
                "files.search",
                "Search Files",
                SkillRiskLevel.Low,
                [SkillPermission.FileSystemRead],
                [
                    RequiredString("directoryPath", "Directory path to search."),
                    RequiredString("searchPattern", "File name pattern."),
                    OptionalBool("recursive", "Search subdirectories.")
                ]),
            Create(
                "files.search_content",
                "Search File Content",
                SkillRiskLevel.Low,
                [SkillPermission.FileSystemRead],
                [
                    RequiredString("directoryPath", "Directory path to search."),
                    RequiredString("query", "Text to search for."),
                    OptionalString("searchPattern", "File pattern such as *.md or *.cs."),
                    OptionalBool("recursive", "Search subdirectories."),
                    OptionalNumber("maxMatches", "Maximum number of matches to return.")
                ]),
            Create(
                "files.tree",
                "Directory Tree",
                SkillRiskLevel.Low,
                [SkillPermission.FileSystemRead],
                [
                    RequiredString("directoryPath", "Directory path to inspect."),
                    OptionalNumber("maxDepth", "Maximum tree depth."),
                    OptionalNumber("maxEntries", "Maximum number of entries to return.")
                ]),
            Create(
                "files.read_lines",
                "Read File Lines",
                SkillRiskLevel.Low,
                [SkillPermission.FileSystemRead],
                [
                    RequiredString("filePath", "Full path to the file."),
                    OptionalNumber("startLine", "Starting line number."),
                    OptionalNumber("lineCount", "Number of lines to read.")
                ]),
            Create(
                "file.read",
                "Read File",
                SkillRiskLevel.Low,
                [SkillPermission.FileSystemRead],
                [RequiredString("filePath", "Full path to the file.")]),
            Create(
                "file.read_range",
                "Read File Range",
                SkillRiskLevel.Low,
                [SkillPermission.FileSystemRead],
                [
                    RequiredString("filePath", "Full path to the file."),
                    OptionalNumber("startLine", "Starting line number."),
                    OptionalNumber("lineCount", "Number of lines to read.")
                ]),
            Create(
                "workspace.tree",
                "Workspace Tree",
                SkillRiskLevel.Low,
                [SkillPermission.FileSystemRead],
                [
                    RequiredString("directoryPath", "Workspace or directory path to inspect."),
                    OptionalNumber("maxDepth", "Maximum tree depth."),
                    OptionalNumber("maxEntries", "Maximum number of entries to return.")
                ]),
            Create(
                "workspace.find_files",
                "Find Workspace Files",
                SkillRiskLevel.Low,
                [SkillPermission.FileSystemRead],
                [
                    RequiredString("directoryPath", "Workspace or directory path to search."),
                    RequiredString("searchPattern", "File name pattern."),
                    OptionalBool("recursive", "Search subdirectories.")
                ]),
            Create(
                "workspace.search_text",
                "Search Workspace Text",
                SkillRiskLevel.Low,
                [SkillPermission.FileSystemRead],
                [
                    RequiredString("directoryPath", "Workspace or directory path to search."),
                    RequiredString("query", "Text to search for."),
                    OptionalString("searchPattern", "File pattern such as *.md or *.cs."),
                    OptionalBool("recursive", "Search subdirectories."),
                    OptionalNumber("maxMatches", "Maximum number of matches to return.")
                ]),
            Create(
                "workspace.map",
                "Workspace Map",
                SkillRiskLevel.Low,
                [SkillPermission.FileSystemRead],
                [
                    RequiredString("directoryPath", "Workspace or directory path to inspect."),
                    OptionalNumber("maxDepth", "Maximum tree depth."),
                    OptionalNumber("maxEntries", "Maximum number of entries to return.")
                ]),
            Create(
                "code.search",
                "Search Code",
                SkillRiskLevel.Low,
                [SkillPermission.FileSystemRead],
                [
                    RequiredString("directoryPath", "Workspace or directory path to search."),
                    RequiredString("query", "Code text to search for."),
                    OptionalString("searchPattern", "File pattern such as *.cs or *.ts."),
                    OptionalBool("recursive", "Search subdirectories."),
                    OptionalNumber("maxMatches", "Maximum number of matches to return.")
                ]),
            Create(
                "code.outline",
                "Code Outline",
                SkillRiskLevel.Low,
                [SkillPermission.FileSystemRead],
                [
                    RequiredString("filePath", "Full path to the code file."),
                    OptionalNumber("maxSymbols", "Maximum number of symbols to return.")
                ]),
            Create(
                "files.open",
                "Open File",
                SkillRiskLevel.High,
                [SkillPermission.ProcessLaunch, SkillPermission.FileSystemRead],
                [
                    RequiredString("filePath", "Full path to the file."),
                    OptionalBool("allowExecutables", "Allow executable files.")
                ]),
            Create(
                "files.show_in_explorer",
                "Show In Explorer",
                SkillRiskLevel.Medium,
                [SkillPermission.ProcessLaunch, SkillPermission.FileSystemRead],
                [RequiredString("path", "File or directory path to reveal.")]),
            Create(
                "directories.create",
                "Create Directory",
                SkillRiskLevel.Medium,
                [SkillPermission.FileSystemWrite],
                [
                    RequiredString("directoryPath", "Full path of the directory."),
                    OptionalBool("openAfterCreation", "Open folder after creation.")
                ]),
            Create(
                "directories.open",
                "Open Directory",
                SkillRiskLevel.Medium,
                [SkillPermission.ProcessLaunch, SkillPermission.FileSystemRead],
                [
                    RequiredString("directoryPath", "Directory path to open."),
                    OptionalString("selectFile", "File to select in the directory.")
                ]),
            Create(
                "clipboard.get",
                "Get Clipboard",
                SkillRiskLevel.Medium,
                [SkillPermission.ClipboardRead],
                [OptionalNumber("maxLength", "Maximum characters to return.")],
                timeoutMilliseconds: 5000),
            Create(
                "clipboard.peek",
                "Peek Clipboard",
                SkillRiskLevel.Medium,
                [SkillPermission.ClipboardRead],
                [OptionalNumber("maxLength", "Maximum characters to return.")],
                timeoutMilliseconds: 5000),
            Create(
                "clipboard.set",
                "Set Clipboard",
                SkillRiskLevel.High,
                [SkillPermission.ClipboardWrite],
                [RequiredString("content", "Text content to put in the clipboard.")],
                timeoutMilliseconds: 5000),
            Create(
                "clipboard.clear",
                "Clear Clipboard",
                SkillRiskLevel.High,
                [SkillPermission.ClipboardWrite],
                timeoutMilliseconds: 5000),
            Create(
                "web.search",
                "Search Web",
                SkillRiskLevel.Medium,
                [SkillPermission.Network],
                [
                    RequiredString("query", "Search query."),
                    OptionalString("lang", "Language code."),
                    OptionalNumber("results", "Maximum result count.")
                ],
                timeoutMilliseconds: 20000),
            Create(
                "web.fetch",
                "Fetch Web URL",
                SkillRiskLevel.Medium,
                [SkillPermission.Network],
                [
                    RequiredString("url", "HTTP or HTTPS URL to fetch."),
                    OptionalNumber("maxLength", "Maximum characters to return."),
                    OptionalNumber("timeoutMilliseconds", "Per-request timeout in milliseconds.")
                ],
                timeoutMilliseconds: 15000),
            Create(
                "web.read_page",
                "Read Web Page",
                SkillRiskLevel.Medium,
                [SkillPermission.Network],
                [
                    RequiredString("url", "HTTP or HTTPS URL to read."),
                    OptionalNumber("maxLength", "Maximum readable text characters to return."),
                    OptionalNumber("timeoutMilliseconds", "Per-request timeout in milliseconds.")
                ],
                timeoutMilliseconds: 15000),
            Create(
                "shell.run",
                "Run Shell Command",
                SkillRiskLevel.High,
                [SkillPermission.ProcessLaunch],
                [
                    RequiredString("command", "Non-interactive shell command to execute."),
                    OptionalString("workingDirectory", "Existing working directory."),
                    OptionalNumber("timeoutMilliseconds", "Command timeout in milliseconds."),
                    OptionalNumber("maxOutputLength", "Maximum stdout/stderr characters to return.")
                ],
                timeoutMilliseconds: 15000),
            Create(
                "window.active",
                "Active Window Context",
                SkillRiskLevel.Low,
                [SkillPermission.SystemInformation],
                timeoutMilliseconds: 5000),
            Create(
                "window.list",
                "Visible Window List",
                SkillRiskLevel.Low,
                [SkillPermission.SystemInformation],
                [OptionalNumber("maxWindows", "Maximum visible windows to return.")],
                timeoutMilliseconds: 5000),
            Create(
                "accessibility.tree",
                "Accessibility Tree Snapshot",
                SkillRiskLevel.Low,
                [SkillPermission.SystemInformation],
                [
                    OptionalNumber("maxNodes", "Maximum OCR/object nodes to return."),
                    OptionalNumber("maxScreens", "Maximum screens to inspect."),
                    OptionalBool("includeObjects", "Include detected UI objects.")
                ],
                timeoutMilliseconds: 15000),
            Create(
                "communication.email.send",
                "Send Email",
                SkillRiskLevel.High,
                [SkillPermission.Network],
                [
                    RequiredString("to", "Recipient email address."),
                    RequiredString("subject", "Email subject."),
                    RequiredString("body", "Email body."),
                    OptionalBool("isHtml", "Treat body as HTML.")
                ]),
            Create(
                "communication.email.template.send",
                "Send Email Template",
                SkillRiskLevel.High,
                [SkillPermission.Network],
                [
                    RequiredString("to", "Recipient email address."),
                    RequiredString("templateName", "Template name."),
                    RequiredString("templateDataJson", "Template data as JSON.")
                ]),
            Create(
                "communication.email.validate",
                "Validate Email",
                SkillRiskLevel.Low,
                [SkillPermission.None],
                [RequiredString("email", "Email address to validate.")]),
            Create(
                "communication.sms.send",
                "Send SMS",
                SkillRiskLevel.High,
                [SkillPermission.Network],
                [
                    RequiredString("to", "Recipient phone number."),
                    RequiredString("message", "SMS message body.")
                ]),
            Create(
                "communication.sms.validate",
                "Validate Phone Number",
                SkillRiskLevel.Low,
                [SkillPermission.None],
                [RequiredString("phoneNumber", "Phone number to validate.")]),
            Create(
                "communication.sms.status",
                "Check SMS Status",
                SkillRiskLevel.Low,
                [SkillPermission.Network])
        ];
    }

    private static KamSkillManifest Create(
        string id,
        string displayName,
        SkillRiskLevel riskLevel,
        IReadOnlyCollection<SkillPermission> permissions,
        IReadOnlyCollection<SkillArgumentDefinition>? arguments = null,
        int timeoutMilliseconds = 10000)
    {
        return new KamSkillManifest
        {
            Id = id,
            DisplayName = displayName,
            Source = "builtin",
            ExecutorType = "builtin",
            Enabled = true,
            ReviewRequired = false,
            RiskLevel = riskLevel,
            Permissions = permissions.ToList(),
            GrantedPermissions = permissions
                .Where(permission => permission != SkillPermission.None)
                .Distinct()
                .ToList(),
            Arguments = arguments?.ToList() ?? [],
            TimeoutMilliseconds = timeoutMilliseconds
        };
    }

    private static SkillArgumentDefinition RequiredString(string name, string description) =>
        Argument(name, description, SkillArgumentType.String, required: true);

    private static SkillArgumentDefinition OptionalString(string name, string description) =>
        Argument(name, description, SkillArgumentType.String, required: false);

    private static SkillArgumentDefinition OptionalBool(string name, string description) =>
        Argument(name, description, SkillArgumentType.Boolean, required: false);

    private static SkillArgumentDefinition OptionalNumber(string name, string description) =>
        Argument(name, description, SkillArgumentType.Number, required: false);

    private static SkillArgumentDefinition Argument(
        string name,
        string description,
        SkillArgumentType type,
        bool required)
    {
        return new SkillArgumentDefinition
        {
            Name = name,
            Description = description,
            Type = type,
            Required = required
        };
    }
}
