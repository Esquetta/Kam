using System.Text.Json;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Infrastructure.Agent.Extensions;

namespace SmartVoiceAgent.Infrastructure.Agent.Evaluation;

/// <summary>
/// A throwaway workspace for one task, and the approval gate that keeps the agent inside it.
/// It stands in for the user: file tools may run when every path argument is inside the
/// workspace and no window is opened; everything else (shell, apps, messaging, the web, paths
/// outside) is refused.
/// </summary>
public sealed class AgentTaskSandbox : IToolPermissionService
{
    // Built-in file skills, by tool-name prefix (skill ids with '.' replaced by '_').
    private static readonly string[] AllowedPrefixes = ["files_", "file_", "workspace_", "code_", "directories_"];

    // File skills that open a window instead of working on files.
    private static readonly HashSet<string> OpensWindow = new(StringComparer.Ordinal)
    {
        "files_open",
        "files_show_in_explorer",
        "directories_open"
    };

    private readonly string _baseDirectory;
    private readonly List<string> _blocked = [];

    /// <summary>
    /// Creates the sandbox.
    /// </summary>
    /// <param name="baseDirectory">Folder under which each task gets its own workspace.</param>
    public AgentTaskSandbox(string baseDirectory)
    {
        _baseDirectory = baseDirectory;
    }

    /// <summary>The current task's workspace, or null between tasks.</summary>
    public string? Root { get; private set; }

    /// <summary>Calls refused since the task began.</summary>
    public int BlockedCalls
    {
        get
        {
            lock (_blocked)
            {
                return _blocked.Count;
            }
        }
    }

    /// <summary>Each refused call since the task began: tool name and arguments.</summary>
    public IReadOnlyList<string> BlockedDetails
    {
        get
        {
            lock (_blocked)
            {
                return [.. _blocked];
            }
        }
    }

    /// <inheritdoc />
    public ApprovalMode Mode { get; set; } = ApprovalMode.FullAuto;

    /// <inheritdoc />
    public IReadOnlyCollection<string> AllowRules => [];

    /// <inheritdoc />
    public IReadOnlyCollection<string> DenyRules => [];

    /// <summary>
    /// Creates a fresh workspace and writes the task's files into it.
    /// </summary>
    /// <param name="files">Workspace-relative path to contents.</param>
    public string Begin(IReadOnlyDictionary<string, string> files)
    {
        var root = Path.Combine(_baseDirectory, Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        foreach (var (relativePath, contents) in files)
        {
            var path = Resolve(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }

        Root = root;
        lock (_blocked)
        {
            _blocked.Clear();
        }

        return root;
    }

    /// <summary>
    /// Deletes the current workspace. A file another process still holds (a virus scanner, the
    /// search indexer) leaves the folder behind instead of stopping the run.
    /// </summary>
    public void End()
    {
        if (Root is not null)
        {
            TryDelete(Root);
        }

        Root = null;
    }

    /// <summary>
    /// Deletes a folder, retrying briefly; gives up quietly when files stay locked.
    /// </summary>
    /// <param name="directory">The folder.</param>
    public static void TryDelete(string directory)
    {
        for (var attempt = 1; attempt <= 3 && Directory.Exists(directory); attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(200 * attempt);
            }
        }
    }

    /// <summary>
    /// Resolves a workspace-relative path and refuses one that leaves the workspace.
    /// </summary>
    /// <param name="root">The workspace.</param>
    /// <param name="relativePath">The path inside it.</param>
    public static string Resolve(string root, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!ExtensionPaths.IsInside(root, path))
        {
            throw new InvalidDataException($"Path '{relativePath}' leaves the workspace.");
        }

        return path;
    }

    /// <inheritdoc />
    public ToolPermissionDecision Evaluate(AgentToolDescriptor tool, string argumentsJson)
    {
        var allowed = Root is not null
            && AllowedPrefixes.Any(prefix => tool.Name.StartsWith(prefix, StringComparison.Ordinal))
            && !OpensWindow.Contains(tool.Name)
            && PathsStayInside(Root, argumentsJson);

        if (!allowed)
        {
            var arguments = argumentsJson.Length <= 200 ? argumentsJson : argumentsJson[..200] + "...";
            lock (_blocked)
            {
                _blocked.Add($"{tool.Name} {arguments}");
            }

            return ToolPermissionDecision.Deny;
        }

        return ToolPermissionDecision.Allow;
    }

    /// <inheritdoc />
    public string SuggestAllowRule(AgentToolDescriptor tool, string argumentsJson) => tool.Name;

    /// <inheritdoc />
    public void AddRule(string rule, bool allow)
    {
    }

    /// <inheritdoc />
    public void RemoveRule(string rule)
    {
    }

    /// <summary>
    /// True when every argument that names a path or folder resolves inside <paramref name="root"/>
    /// and no argument asks to open a window. Relative paths resolve against the process folder,
    /// as the file tools do, so they must be absolute.
    /// </summary>
    /// <param name="root">The workspace.</param>
    /// <param name="argumentsJson">The call's arguments.</param>
    public static bool PathsStayInside(string root, string argumentsJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                // openAfterWrite, openAfterCreation, openFolder, showInFolder: these open a window on the desktop.
                var opensWindow = property.Name.Contains("open", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Contains("show", StringComparison.OrdinalIgnoreCase);
                if (opensWindow && property.Value.ValueKind == JsonValueKind.True)
                {
                    return false;
                }

                var isPath = property.Name.Contains("path", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Contains("directory", StringComparison.OrdinalIgnoreCase);
                if (!isPath || property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(value)
                    || !Path.IsPathFullyQualified(value)
                    || !ExtensionPaths.IsInside(root, value))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
