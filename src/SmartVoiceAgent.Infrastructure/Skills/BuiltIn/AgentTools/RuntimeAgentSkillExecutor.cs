using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Agent.Tools;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

public sealed class RuntimeAgentSkillExecutor : ISkillExecutor
{
    public const string SkillId = "agents.run";
    private const int MaxToolObservationChars = 4000;
    private const int MaxReadFileObservationChars = 6000;
    private static readonly Regex PathTokenRegex = new(
        @"(?<path>(?:[\w.-]+[\\/])+[\w.-]+\.[A-Za-z0-9]{1,12}|[\w.-]+\.[A-Za-z0-9]{1,12})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex QuotedSearchRegex = new(
        "(?:search|find|grep|ara|bul)\\s+[\"'`“”‘’](?<query>[^\"'`“”‘’]{2,80})[\"'`“”‘’]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex PlainSearchRegex = new(
        @"(?:search|find|grep|ara|bul)\s+(?<query>[A-Za-z0-9_.:-]{2,80})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly IRuntimeAgentFactory _runtimeAgentFactory;
    private readonly FileAgentTools? _fileTools;
    private readonly ISkillConfirmationService? _confirmationService;

    public RuntimeAgentSkillExecutor(
        IRuntimeAgentFactory runtimeAgentFactory,
        FileAgentTools? fileTools = null,
        ISkillConfirmationService? confirmationService = null)
    {
        _runtimeAgentFactory = runtimeAgentFactory;
        _fileTools = fileTools;
        _confirmationService = confirmationService;
    }

    public bool CanExecute(string skillId)
    {
        return skillId.Equals(SkillId, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (!CanExecute(plan.SkillId))
        {
            return SkillResult.Failed($"Unsupported agent skill: {plan.SkillId}");
        }

        var task = SkillPlanArgumentReader.GetString(plan, "task");
        if (string.IsNullOrWhiteSpace(task))
        {
            return SkillResult.Failed(
                "Argument 'task' is required.",
                SkillExecutionStatus.ValidationFailed,
                "validation_failed");
        }

        var role = SkillPlanArgumentReader.GetString(plan, "role", "general");
        var agentName = SkillPlanArgumentReader.GetString(plan, "agentName");
        if (string.IsNullOrWhiteSpace(agentName))
        {
            agentName = CreateAgentName(role);
        }

        var observations = await CreateReadOnlyToolContextAsync(role, task, cancellationToken)
            .ConfigureAwait(false);

        var result = await _runtimeAgentFactory
            .RunAsync(new RuntimeAgentRequest(agentName, role, task, observations), cancellationToken)
            .ConfigureAwait(false);

        var queuedActions = await QueueApprovalGatedActionsAsync(result, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(queuedActions.ErrorMessage))
        {
            return SkillResult.Failed(
                queuedActions.ErrorMessage,
                SkillExecutionStatus.ValidationFailed,
                "agent_action_request_invalid");
        }

        var message = queuedActions.QueuedCount == 0
            ? result.Response
            : $"{result.Response}{Environment.NewLine}{queuedActions.Message}";

        return SkillResult.Succeeded(message.Trim(), result);
    }

    private async Task<QueuedActionResult> QueueApprovalGatedActionsAsync(
        RuntimeAgentResult result,
        CancellationToken cancellationToken)
    {
        if (result.ActionRequests is not { Count: > 0 })
        {
            return QueuedActionResult.None;
        }

        if (_confirmationService is null)
        {
            return new QueuedActionResult(
                0,
                string.Empty,
                "Runtime agent proposed actions, but approval handling is not available.");
        }

        var queued = 0;
        var messages = new List<string>();
        foreach (var request in result.ActionRequests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var queueResult = await QueueApprovalGatedActionAsync(result, request)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(queueResult.ErrorMessage))
            {
                return queueResult;
            }

            queued += queueResult.QueuedCount;
            if (!string.IsNullOrWhiteSpace(queueResult.Message))
            {
                messages.Add(queueResult.Message);
            }
        }

        return new QueuedActionResult(
            queued,
            queued == 0
                ? string.Empty
                : $"Approval required: {queued} action(s) were added to the review queue. {string.Join(" ", messages)}",
            string.Empty);
    }

    private async Task<QueuedActionResult> QueueApprovalGatedActionAsync(
        RuntimeAgentResult result,
        RuntimeAgentActionRequest request)
    {
        if (request.Action.Equals("file.patch", StringComparison.OrdinalIgnoreCase))
        {
            return await QueueFilePatchActionAsync(result, request).ConfigureAwait(false);
        }

        if (request.Action.Equals("tests.run", StringComparison.OrdinalIgnoreCase))
        {
            return QueueTestRunAction(result, request);
        }

        return new QueuedActionResult(
            0,
            string.Empty,
            $"Runtime agent requested unsupported action '{request.Action}'.");
    }

    private async Task<QueuedActionResult> QueueFilePatchActionAsync(
        RuntimeAgentResult result,
        RuntimeAgentActionRequest request)
    {
        if (_fileTools is null)
        {
            return new QueuedActionResult(0, string.Empty, "Runtime agent requested a file patch, but workspace file tools are not available.");
        }

        if (string.IsNullOrWhiteSpace(request.FilePath)
            || request.OldText is null
            || request.NewText is null)
        {
            return new QueuedActionResult(0, string.Empty, "Runtime agent requested an incomplete file patch.");
        }

        var filePath = ResolveWorkspacePath(_fileTools.DefaultWorkingDirectory, request.FilePath);
        if (filePath is null)
        {
            return new QueuedActionResult(0, string.Empty, "Runtime agent requested a file patch outside the workspace or for a missing file.");
        }

        var expectedOccurrences = Math.Max(1, request.ExpectedOccurrences);
        var preview = await _fileTools
            .PatchFileAsync(filePath, request.OldText, request.NewText, expectedOccurrences, previewOnly: true)
            .ConfigureAwait(false);
        if (LooksLikeToolError(preview))
        {
            return new QueuedActionResult(0, string.Empty, $"Runtime agent file patch preview failed: {preview}");
        }

        var plan = SkillPlan.FromObject(
            "file.patch",
            new
            {
                filePath,
                oldText = request.OldText,
                newText = request.NewText,
                expectedOccurrences
            });
        plan.RequiresConfirmation = true;
        plan.Reasoning = "Runtime agent proposed a file patch. Review the diff before applying changes.";

        var confirmation = _confirmationService!.Queue(
            $"Approve runtime agent patch from {result.AgentName}",
            plan,
            plan.Reasoning,
            preview);

        return new QueuedActionResult(
            1,
            $"Patch approval queued ({confirmation.Id}).",
            string.Empty);
    }

    private QueuedActionResult QueueTestRunAction(
        RuntimeAgentResult result,
        RuntimeAgentActionRequest request)
    {
        if (_fileTools is null)
        {
            return new QueuedActionResult(0, string.Empty, "Runtime agent requested a test run, but workspace context is not available.");
        }

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return new QueuedActionResult(0, string.Empty, "Runtime agent requested an empty test command.");
        }

        var plan = SkillPlan.FromObject(
            "shell.run",
            new
            {
                command = request.Command,
                workingDirectory = _fileTools.DefaultWorkingDirectory,
                timeoutMilliseconds = 15000,
                maxOutputLength = 12000
            });
        plan.RequiresConfirmation = true;
        plan.Reasoning = "Runtime agent proposed a test command. Approve it before launching a process.";

        var confirmation = _confirmationService!.Queue(
            $"Approve runtime agent test from {result.AgentName}",
            plan,
            plan.Reasoning,
            FormatTestPreview(request.Command, _fileTools.DefaultWorkingDirectory));

        return new QueuedActionResult(
            1,
            $"Test approval queued ({confirmation.Id}).",
            string.Empty);
    }

    private async Task<IReadOnlyList<RuntimeAgentToolObservation>?> CreateReadOnlyToolContextAsync(
        string role,
        string task,
        CancellationToken cancellationToken)
    {
        if (_fileTools is null || !ShouldAttachWorkspaceContext(role, task))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var workspaceMap = await _fileTools
            .DescribeWorkspaceAsync(_fileTools.DefaultWorkingDirectory, maxDepth: 2, maxEntries: 160)
            .ConfigureAwait(false);

        var observations = new List<RuntimeAgentToolObservation>
        {
            new RuntimeAgentToolObservation(
                "workspace.map",
                Truncate(workspaceMap, MaxToolObservationChars),
                !LooksLikeToolError(workspaceMap))
        };

        foreach (var path in ExtractCandidateFilePaths(task).Take(2))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = ResolveWorkspacePath(_fileTools.DefaultWorkingDirectory, path);
            if (filePath is null)
            {
                continue;
            }

            var fileContent = await _fileTools.ReadLinesAsync(filePath, startLine: 1, lineCount: 120)
                .ConfigureAwait(false);
            observations.Add(new RuntimeAgentToolObservation(
                "file.read_lines",
                Truncate(fileContent, MaxReadFileObservationChars),
                !LooksLikeToolError(fileContent)));
        }

        var query = ExtractSearchQuery(task);
        if (!string.IsNullOrWhiteSpace(query))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var searchResult = await _fileTools.SearchFileContentAsync(
                    _fileTools.DefaultWorkingDirectory,
                    query,
                    searchPattern: "*.*",
                    recursive: true,
                    maxMatches: 12)
                .ConfigureAwait(false);
            observations.Add(new RuntimeAgentToolObservation(
                "workspace.search_text",
                Truncate(searchResult, MaxToolObservationChars),
                !LooksLikeToolError(searchResult)));
        }

        if (ShouldAttachDiffContext(role, task))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var diffSummary = await _fileTools.GetGitDiffSummaryAsync()
                .ConfigureAwait(false);
            observations.Add(new RuntimeAgentToolObservation(
                "git.diff_summary",
                Truncate(diffSummary, MaxToolObservationChars),
                !LooksLikeToolError(diffSummary)));
        }

        return observations;
    }

    private static bool ShouldAttachWorkspaceContext(string role, string task)
    {
        var value = $"{role} {task}";
        return ContainsAny(
            value,
            "coding",
            "code",
            "repo",
            "repository",
            "workspace",
            "worktree",
            "project",
            "file",
            "diff",
            "test",
            "build",
            "github");
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldAttachDiffContext(string role, string task)
    {
        var value = $"{role} {task}";
        return ContainsAny(value, "diff", "review", "changes", "changed", "git status", "git diff");
    }

    private static bool LooksLikeToolError(string value)
    {
        return value.StartsWith("Hata:", StringComparison.OrdinalIgnoreCase)
            || value.Contains("hatası:", StringComparison.OrdinalIgnoreCase)
            || value.Contains("error:", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(empty)";
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "...";
    }

    private static IReadOnlyList<string> ExtractCandidateFilePaths(string task)
    {
        if (string.IsNullOrWhiteSpace(task))
        {
            return [];
        }

        return PathTokenRegex
            .Matches(task)
            .Select(match => match.Groups["path"].Value.Trim('\'', '"', '`', ',', '.', ';', ':', ')', ']', '}'))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveWorkspacePath(string workspaceRoot, string candidate)
    {
        try
        {
            var root = Path.GetFullPath(workspaceRoot);
            var fullPath = Path.IsPathRooted(candidate)
                ? Path.GetFullPath(candidate)
                : Path.GetFullPath(Path.Combine(root, candidate));

            return IsSameOrChildPath(root, fullPath) && File.Exists(fullPath)
                ? fullPath
                : null;
        }
        catch (Exception ex) when (ex is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return null;
        }
    }

    private static string FormatTestPreview(string command, string workingDirectory)
    {
        var builder = new StringBuilder()
            .AppendLine("Test command preview:")
            .AppendLine($"Working directory: {workingDirectory}")
            .AppendLine($"Command: {command.Trim()}");

        return builder.ToString().TrimEnd();
    }

    private static bool IsSameOrChildPath(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        return normalizedCandidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(
                normalizedRoot + Path.AltDirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractSearchQuery(string task)
    {
        if (string.IsNullOrWhiteSpace(task))
        {
            return null;
        }

        var quoted = QuotedSearchRegex.Match(task);
        if (quoted.Success)
        {
            return quoted.Groups["query"].Value.Trim();
        }

        var plain = PlainSearchRegex.Match(task);
        return plain.Success ? plain.Groups["query"].Value.Trim() : null;
    }

    private static string CreateAgentName(string role)
    {
        var normalized = new string(
            role
                .Where(char.IsLetterOrDigit)
                .Take(32)
                .ToArray());

        return string.IsNullOrWhiteSpace(normalized)
            ? "TaskAgent"
            : $"{normalized}Agent";
    }

    private sealed record QueuedActionResult(
        int QueuedCount,
        string Message,
        string ErrorMessage)
    {
        public static QueuedActionResult None { get; } = new(0, string.Empty, string.Empty);
    }
}
