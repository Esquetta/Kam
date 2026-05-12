using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Infrastructure.Agent.Tools;

namespace SmartVoiceAgent.Infrastructure.Agent.Agents;

public sealed class FileRuntimeAgentReadOnlyToolService : IRuntimeAgentReadOnlyToolService
{
    private const int MaxRequests = 3;
    private const int MaxObservationChars = 4000;
    private const int MaxFileReadChars = 6000;

    private readonly FileAgentTools _fileTools;

    public FileRuntimeAgentReadOnlyToolService(FileAgentTools fileTools)
    {
        _fileTools = fileTools;
    }

    public async Task<IReadOnlyList<RuntimeAgentToolObservation>> ExecuteAsync(
        IReadOnlyList<RuntimeAgentReadOnlyToolRequest> requests,
        CancellationToken cancellationToken = default)
    {
        var observations = new List<RuntimeAgentToolObservation>();
        foreach (var request in requests.Take(MaxRequests))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tool = NormalizeTool(request.Tool);
            var observation = tool switch
            {
                "file.read_lines" => await ReadFileLinesAsync(request),
                "workspace.search_text" => await SearchTextAsync(request),
                "git.diff_summary" => await GitDiffSummaryAsync(),
                _ => new RuntimeAgentToolObservation(
                    string.IsNullOrWhiteSpace(request.Tool) ? "unknown" : request.Tool.Trim(),
                    $"Read-only tool '{request.Tool}' is not available.",
                    false)
            };
            observations.Add(observation);
        }

        return observations;
    }

    private async Task<RuntimeAgentToolObservation> ReadFileLinesAsync(RuntimeAgentReadOnlyToolRequest request)
    {
        var path = ResolveWorkspacePath(request.Path);
        if (path is null)
        {
            return new RuntimeAgentToolObservation(
                "file.read_lines",
                "File path is missing, outside the workspace, or does not exist.",
                false);
        }

        var result = await _fileTools.ReadLinesAsync(path, startLine: 1, lineCount: 120);
        return new RuntimeAgentToolObservation(
            "file.read_lines",
            Truncate(result, MaxFileReadChars),
            !LooksLikeToolError(result));
    }

    private async Task<RuntimeAgentToolObservation> SearchTextAsync(RuntimeAgentReadOnlyToolRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new RuntimeAgentToolObservation(
                "workspace.search_text",
                "Search query is required.",
                false);
        }

        var result = await _fileTools.SearchFileContentAsync(
            _fileTools.DefaultWorkingDirectory,
            request.Query.Trim(),
            searchPattern: "*.*",
            recursive: true,
            maxMatches: 12);
        return new RuntimeAgentToolObservation(
            "workspace.search_text",
            Truncate(result, MaxObservationChars),
            !LooksLikeToolError(result));
    }

    private async Task<RuntimeAgentToolObservation> GitDiffSummaryAsync()
    {
        var result = await _fileTools.GetGitDiffSummaryAsync();
        return new RuntimeAgentToolObservation(
            "git.diff_summary",
            Truncate(result, MaxObservationChars),
            !LooksLikeToolError(result));
    }

    private string? ResolveWorkspacePath(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        try
        {
            var root = Path.GetFullPath(_fileTools.DefaultWorkingDirectory);
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

    private static string NormalizeTool(string tool)
    {
        return string.IsNullOrWhiteSpace(tool) ? string.Empty : tool.Trim().ToLowerInvariant();
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

    private static bool LooksLikeToolError(string value)
    {
        return value.StartsWith("Hata:", StringComparison.OrdinalIgnoreCase)
            || value.Contains("error:", StringComparison.OrdinalIgnoreCase)
            || value.Contains("hatas", StringComparison.OrdinalIgnoreCase);
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
}
