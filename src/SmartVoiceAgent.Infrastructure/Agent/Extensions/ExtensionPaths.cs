namespace SmartVoiceAgent.Infrastructure.Agent.Extensions;

/// <summary>
/// Where Kam keeps user extensions: <c>%AppData%/Kam/skills</c>, <c>commands</c>, <c>plugins</c> and the
/// matching folders inside a workspace.
/// </summary>
public static class ExtensionPaths
{
    /// <summary>Gets <c>%AppData%/Kam</c>.</summary>
    public static string AppDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Kam");

    /// <summary>
    /// Returns the folders a workspace may keep extensions in: <c>.kam/{kind}</c> and <c>.claude/{kind}</c>.
    /// </summary>
    /// <param name="workspaceRoot">The workspace, if one is selected.</param>
    /// <param name="kind"><c>skills</c> or <c>commands</c>.</param>
    public static IEnumerable<string> WorkspaceFolders(string? workspaceRoot, string kind)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            yield break;
        }

        yield return Path.Combine(workspaceRoot, ".kam", kind);
        yield return Path.Combine(workspaceRoot, ".claude", kind);
    }

    /// <summary>
    /// Returns whether <paramref name="path"/> is <paramref name="root"/> or inside it.
    /// </summary>
    /// <param name="root">The folder.</param>
    /// <param name="path">The path to check.</param>
    public static bool IsInside(string root, string path)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return fullPath.Equals(fullRoot, comparison)
            || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
    }

    /// <summary>
    /// Copies a folder, skipping <c>.git</c>.
    /// </summary>
    /// <param name="source">The folder to copy.</param>
    /// <param name="destination">Where to put it; replaced when it exists.</param>
    public static void CopyDirectory(string source, string destination)
    {
        var staging = destination + ".installing";
        DeleteDirectory(staging);
        Directory.CreateDirectory(staging);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            if (!IsGitPath(relative))
            {
                Directory.CreateDirectory(Path.Combine(staging, relative));
            }
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (!IsGitPath(relative))
            {
                File.Copy(file, Path.Combine(staging, relative), overwrite: true);
            }
        }

        DeleteDirectory(destination);
        Directory.Move(staging, destination);
    }

    /// <summary>
    /// Deletes a folder, including read-only files such as git objects.
    /// </summary>
    /// <param name="directory">The folder.</param>
    public static void DeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(directory, recursive: true);
    }

    /// <summary>
    /// Makes a name safe to use as a folder name.
    /// </summary>
    /// <param name="name">The name.</param>
    public static string ToFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Trim().Select(character => invalid.Contains(character) || character is '/' or '\\' or ':' ? '-' : character).ToArray());
        safe = safe.Trim('.', ' ', '-');
        return safe.Length == 0 ? "extension" : safe;
    }

    private static bool IsGitPath(string relative) =>
        relative.Equals(".git", StringComparison.OrdinalIgnoreCase)
        || relative.StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || relative.StartsWith(".git" + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
