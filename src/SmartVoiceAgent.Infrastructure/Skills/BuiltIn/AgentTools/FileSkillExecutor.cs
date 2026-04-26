using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Agent.Tools;

namespace SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

public sealed class FileSkillExecutor : ISkillExecutor
{
    private static readonly HashSet<string> SkillIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "files.read",
        "files.write",
        "files.create",
        "files.delete",
        "files.copy",
        "files.move",
        "files.exists",
        "files.info",
        "files.list",
        "files.search",
        "files.search_content",
        "files.tree",
        "files.read_lines",
        "file.read",
        "file.read_range",
        "workspace.tree",
        "workspace.find_files",
        "workspace.search_text",
        "workspace.map",
        "code.search",
        "code.outline",
        "files.open",
        "files.show_in_explorer",
        "directories.create",
        "directories.open"
    };

    private readonly FileAgentTools _tools;

    public FileSkillExecutor(FileAgentTools tools)
    {
        _tools = tools;
    }

    public bool CanExecute(string skillId)
    {
        return SkillIds.Contains(skillId);
    }

    public async Task<SkillResult> ExecuteAsync(SkillPlan plan, CancellationToken cancellationToken = default)
    {
        var result = plan.SkillId.ToLowerInvariant() switch
        {
            "files.read" => await _tools.ReadFileAsync(SkillPlanArgumentReader.GetString(plan, "filePath")),
            "files.write" => await _tools.WriteFileAsync(
                SkillPlanArgumentReader.GetString(plan, "filePath"),
                SkillPlanArgumentReader.GetString(plan, "content"),
                SkillPlanArgumentReader.GetBool(plan, "append"),
                SkillPlanArgumentReader.GetBool(plan, "openAfterWrite")),
            "files.create" => await _tools.CreateFileAsync(
                SkillPlanArgumentReader.GetString(plan, "filePath"),
                SkillPlanArgumentReader.GetString(plan, "content"),
                SkillPlanArgumentReader.GetBool(plan, "openAfterCreation")),
            "files.delete" => await _tools.DeleteFileAsync(SkillPlanArgumentReader.GetString(plan, "filePath")),
            "files.copy" => await _tools.CopyFileAsync(
                SkillPlanArgumentReader.GetString(plan, "sourcePath"),
                SkillPlanArgumentReader.GetString(plan, "destinationPath"),
                SkillPlanArgumentReader.GetBool(plan, "overwrite"),
                SkillPlanArgumentReader.GetBool(plan, "showInFolder")),
            "files.move" => await _tools.MoveFileAsync(
                SkillPlanArgumentReader.GetString(plan, "sourcePath"),
                SkillPlanArgumentReader.GetString(plan, "destinationPath"),
                SkillPlanArgumentReader.GetBool(plan, "overwrite")),
            "files.exists" => await _tools.FileExistsAsync(SkillPlanArgumentReader.GetString(plan, "filePath")),
            "files.info" => await _tools.GetFileInfoAsync(SkillPlanArgumentReader.GetString(plan, "filePath")),
            "files.list" => await _tools.ListFilesAsync(
                SkillPlanArgumentReader.GetString(plan, "directoryPath"),
                SkillPlanArgumentReader.GetString(plan, "searchPattern", "*.*"),
                SkillPlanArgumentReader.GetBool(plan, "recursive"),
                SkillPlanArgumentReader.GetBool(plan, "openFolder")),
            "files.search" => await _tools.SearchFilesAsync(
                SkillPlanArgumentReader.GetString(plan, "directoryPath"),
                SkillPlanArgumentReader.GetString(plan, "searchPattern"),
                SkillPlanArgumentReader.GetBool(plan, "recursive", true)),
            "files.search_content" => await _tools.SearchFileContentAsync(
                SkillPlanArgumentReader.GetString(plan, "directoryPath"),
                SkillPlanArgumentReader.GetString(plan, "query"),
                SkillPlanArgumentReader.GetString(plan, "searchPattern", "*.*"),
                SkillPlanArgumentReader.GetBool(plan, "recursive", true),
                SkillPlanArgumentReader.GetInt(plan, "maxMatches", 50)),
            "files.tree" => await _tools.ListDirectoryTreeAsync(
                SkillPlanArgumentReader.GetString(plan, "directoryPath"),
                SkillPlanArgumentReader.GetInt(plan, "maxDepth", 2),
                SkillPlanArgumentReader.GetInt(plan, "maxEntries", 200)),
            "files.read_lines" => await _tools.ReadLinesAsync(
                SkillPlanArgumentReader.GetString(plan, "filePath"),
                SkillPlanArgumentReader.GetInt(plan, "startLine", 1),
                SkillPlanArgumentReader.GetInt(plan, "lineCount")),
            "file.read" => await _tools.ReadFileAsync(SkillPlanArgumentReader.GetString(plan, "filePath")),
            "file.read_range" => await _tools.ReadLinesAsync(
                SkillPlanArgumentReader.GetString(plan, "filePath"),
                SkillPlanArgumentReader.GetInt(plan, "startLine", 1),
                SkillPlanArgumentReader.GetInt(plan, "lineCount")),
            "workspace.tree" => await _tools.ListDirectoryTreeAsync(
                SkillPlanArgumentReader.GetString(plan, "directoryPath"),
                SkillPlanArgumentReader.GetInt(plan, "maxDepth", 2),
                SkillPlanArgumentReader.GetInt(plan, "maxEntries", 200)),
            "workspace.find_files" => await _tools.SearchFilesAsync(
                SkillPlanArgumentReader.GetString(plan, "directoryPath"),
                SkillPlanArgumentReader.GetString(plan, "searchPattern"),
                SkillPlanArgumentReader.GetBool(plan, "recursive", true)),
            "workspace.search_text" => await _tools.SearchFileContentAsync(
                SkillPlanArgumentReader.GetString(plan, "directoryPath"),
                SkillPlanArgumentReader.GetString(plan, "query"),
                SkillPlanArgumentReader.GetString(plan, "searchPattern", "*.*"),
                SkillPlanArgumentReader.GetBool(plan, "recursive", true),
                SkillPlanArgumentReader.GetInt(plan, "maxMatches", 50)),
            "workspace.map" => await _tools.DescribeWorkspaceAsync(
                SkillPlanArgumentReader.GetString(plan, "directoryPath"),
                SkillPlanArgumentReader.GetInt(plan, "maxDepth", 2),
                SkillPlanArgumentReader.GetInt(plan, "maxEntries", 200)),
            "code.search" => await _tools.SearchFileContentAsync(
                SkillPlanArgumentReader.GetString(plan, "directoryPath"),
                SkillPlanArgumentReader.GetString(plan, "query"),
                SkillPlanArgumentReader.GetString(plan, "searchPattern", "*.*"),
                SkillPlanArgumentReader.GetBool(plan, "recursive", true),
                SkillPlanArgumentReader.GetInt(plan, "maxMatches", 50)),
            "code.outline" => await _tools.OutlineCodeAsync(
                SkillPlanArgumentReader.GetString(plan, "filePath"),
                SkillPlanArgumentReader.GetInt(plan, "maxSymbols", 100)),
            "files.open" => await _tools.OpenFileAsync(
                SkillPlanArgumentReader.GetString(plan, "filePath"),
                SkillPlanArgumentReader.GetBool(plan, "allowExecutables")),
            "files.show_in_explorer" => await _tools.ShowInExplorerAsync(SkillPlanArgumentReader.GetString(plan, "path")),
            "directories.create" => await _tools.CreateDirectoryAsync(
                SkillPlanArgumentReader.GetString(plan, "directoryPath"),
                SkillPlanArgumentReader.GetBool(plan, "openAfterCreation")),
            "directories.open" => await _tools.OpenDirectoryAsync(
                SkillPlanArgumentReader.GetString(plan, "directoryPath"),
                SkillPlanArgumentReader.GetString(plan, "selectFile")),
            _ => null
        };

        return result is null
            ? SkillResult.Failed($"Unsupported file skill: {plan.SkillId}")
            : AgentToolSkillResult.FromMessage(result);
    }
}
