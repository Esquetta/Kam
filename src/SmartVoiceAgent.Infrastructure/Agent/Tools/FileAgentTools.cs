using SmartVoiceAgent.Infrastructure.Security;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Infrastructure.Agent.Tools
{
    /// <summary>
    /// File Agent Tools for file system operations including read, write, create, delete, search, and opening.
    /// </summary>
    public sealed class FileAgentTools
    {
        private const int MaxDiffPreviewLines = 400;

        private readonly string _defaultWorkingDirectory;
        private readonly bool _restrictToDefaultWorkingDirectory;
        private readonly HashSet<string> _allowedExtensions;
        private readonly HashSet<string> _executableExtensions;
        private readonly long _maxFileSizeBytes;

        public string DefaultWorkingDirectory => _defaultWorkingDirectory;

        public FileAgentTools(
            string? defaultWorkingDirectory = null,
            long maxFileSizeBytes = 10 * 1024 * 1024) // 10MB default
        {
            _defaultWorkingDirectory = defaultWorkingDirectory ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
            _restrictToDefaultWorkingDirectory = !string.IsNullOrWhiteSpace(defaultWorkingDirectory);
            _maxFileSizeBytes = maxFileSizeBytes;

            // Define allowed file extensions for security
            _allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".txt", ".md", ".json", ".xml", ".csv", ".log",
                ".html", ".css", ".js", ".ts", ".py", ".cs",
                ".java", ".cpp", ".h", ".yml", ".yaml", ".ini",
                ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
                ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".svg"
            };

            // Dangerous extensions that should not be auto-opened
            _executableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".exe", ".bat", ".cmd", ".sh", ".msi", ".dll", ".com"
            };
        }

        #region Enhanced Existing Methods

        public async Task<string> CreateFileAsync(
            [Description("Full path to the file to create")]
            string filePath,
            [Description("Initial content for the file (optional)")]
            string content = "",
            [Description("If true, opens the file with default application after creation")]
            bool openAfterCreation = false)
        {
            try
            {
                Console.WriteLine($"FileAgent: Creating file {filePath} (openAfterCreation: {openAfterCreation})");

                // Security: Validate path to prevent path traversal
                if (!IsSafeFilePath(filePath))
                {
                    return "Error: invalid file path; the operation was refused for security.";
                }

                if (File.Exists(filePath))
                {
                    return $"Error: file '{filePath}' already exists.";
                }

                var fileInfo = new FileInfo(filePath);

                // Check file extension
                if (!_allowedExtensions.Contains(fileInfo.Extension))
                {
                    return $"Error: '{fileInfo.Extension}' files are not supported.";
                }

                // Create directory if it doesn't exist
                if (fileInfo.Directory != null && !fileInfo.Directory.Exists)
                {
                    fileInfo.Directory.Create();
                }

                await File.WriteAllTextAsync(filePath, content);

                string result = $"File created: {filePath}";

                // Auto-open if requested and safe
                if (openAfterCreation)
                {
                    var openResult = await OpenFileAsync(filePath, true);
                    result += $"\n{openResult}";
                }

                return result;
            }
            catch (Exception ex)
            {
                return $"Error creating file: {ex.Message}";
            }
        }

        public async Task<string> ListFilesAsync(
            [Description("Directory path to list files from")]
            string directoryPath,
            [Description("Search pattern (e.g., '*.txt', '*.json')")]
            string searchPattern = "*.*",
            [Description("If true, searches subdirectories")]
            bool recursive = false,
            [Description("If true, opens the folder in File Explorer after listing")]
            bool openFolder = false)
        {
            try
            {
                if (!IsSafeFilePath(directoryPath))
                {
                    return "Error: invalid directory path; the operation was refused for security.";
                }

                if (!Directory.Exists(directoryPath))
                {
                    return $"Error: directory '{directoryPath}' not found.";
                }

                var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var files = Directory.GetFiles(directoryPath, searchPattern, searchOption);

                var sb = new StringBuilder();

                if (files.Length == 0)
                {
                    sb.AppendLine($"No files found in '{directoryPath}'.");
                }
                else
                {
                    sb.AppendLine($"Files in directory ({files.Length}):");
                    foreach (var file in files)
                    {
                        var fileInfo = new FileInfo(file);
                        sb.AppendLine($"  - {fileInfo.Name} ({FormatFileSize(fileInfo.Length)})");
                    }
                }

                // Auto-open folder if requested
                if (openFolder)
                {
                    var openResult = await OpenDirectoryAsync(directoryPath);
                    sb.AppendLine($"\nFolder opened: {openResult}");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error listing files: {ex.Message}";
            }
        }

        #endregion

        #region New File Opening Tools

        public async Task<string> OpenFileAsync(
            [Description("Full path to the file to open")]
            string filePath,
            [Description("If true, treats executables as safe (use carefully)")]
            bool allowExecutables = false)
        {
            try
            {
                Console.WriteLine($"FileAgent: Opening file {filePath}");

                // Security: Validate path to prevent path traversal
                if (!IsSafeFilePath(filePath))
                {
                    return "Error: unsafe file path; the path leaves the allowed directory or contains invalid characters.";
                }

                if (!File.Exists(filePath))
                {
                    return $"Error: file '{filePath}' not found.";
                }

                var fileInfo = new FileInfo(filePath);
                var ext = fileInfo.Extension;

                // Security check for executables
                if (_executableExtensions.Contains(ext) && !allowExecutables)
                {
                    return $"Error: '{ext}' files are not opened automatically; run them yourself.";
                }

                // Use ProcessStartInfo for better control
                var psi = new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true, // This opens with default associated application
                    Verb = "open"
                };

                using var process = Process.Start(psi);

                await Task.Delay(500); // Brief delay to ensure process starts

                return $"File opened: {fileInfo.Name} ({ext})";
            }
            catch (Exception ex)
            {
                return $"Error opening file: {ex.Message}";
            }
        }

        public async Task<string> OpenDirectoryAsync(
            [Description("Full path to the directory to open")]
            string directoryPath,
            [Description("If true, selects a specific file in that folder (highlights it)")]
            string? selectFile = null)
        {
            try
            {
                Console.WriteLine($"FileAgent: Opening directory {directoryPath}");

                // Security: Validate path to prevent path traversal
                if (!IsSafeFilePath(directoryPath))
                {
                    return "Error: unsafe directory path; the path leaves the allowed directory or contains invalid characters.";
                }

                if (!Directory.Exists(directoryPath))
                {
                    return $"Error: directory '{directoryPath}' not found.";
                }

                ProcessStartInfo psi;

                if (!string.IsNullOrEmpty(selectFile) && File.Exists(Path.Combine(directoryPath, selectFile)))
                {
                    // Open folder and highlight specific file
                    var fullPath = Path.Combine(directoryPath, selectFile);
                    psi = new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{fullPath}\"",
                        UseShellExecute = false
                    };
                }
                else
                {
                    // Just open the folder
                    psi = new ProcessStartInfo
                    {
                        FileName = directoryPath,
                        UseShellExecute = true,
                        Verb = "open"
                    };
                }

                using var process = Process.Start(psi);
                await Task.Delay(100);

                return $"Folder opened: {directoryPath}";
            }
            catch (Exception ex)
            {
                return $"Error opening folder: {ex.Message}";
            }
        }

        public async Task<string> ShowInExplorerAsync(
            [Description("Full path to the file or folder to show")]
            string path)
        {
            try
            {
                // Security: Validate path to prevent path traversal
                if (!IsSafeFilePath(path))
                {
                    return "Error: unsafe path; the path leaves the allowed directory or contains invalid characters.";
                }

                if (File.Exists(path))
                {
                    var directory = Path.GetDirectoryName(path);
                    var psi = new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{path}\"",
                        UseShellExecute = false
                    };
                    Process.Start(psi);
                    return $"File location shown: {Path.GetFileName(path)}";
                }
                else if (Directory.Exists(path))
                {
                    return await OpenDirectoryAsync(path);
                }
                else
                {
                    return $"Error: '{path}' not found.";
                }
            }
            catch (Exception ex)
            {
                return $"Error in Explorer: {ex.Message}";
            }
        }

        #endregion

        #region Original Methods (Keep existing implementations)

        public async Task<string> ReadFileAsync(
            [Description("Full path to the file to read")]
            string filePath)
        {
            try
            {
                Console.WriteLine($"FileAgent: Reading file {filePath}");

                if (!IsSafeFilePath(filePath))
                {
                    return "Error: invalid file path; the operation was refused for security.";
                }

                if (!File.Exists(filePath))
                {
                    return $"Error: file '{filePath}' not found.";
                }

                var fileInfo = new FileInfo(filePath);

                if (fileInfo.Length > _maxFileSizeBytes)
                {
                    return $"Error: file is too large ({fileInfo.Length / 1024 / 1024}MB). Maximum size: {_maxFileSizeBytes / 1024 / 1024}MB";
                }

                if (!_allowedExtensions.Contains(fileInfo.Extension))
                {
                    return $"Error: '{fileInfo.Extension}' files are not supported.";
                }

                var content = await File.ReadAllTextAsync(filePath);
                return $"File read:\n\n{content}";
            }
            catch (UnauthorizedAccessException)
            {
                return $"Error: access to file '{filePath}' was denied.";
            }
            catch (Exception ex)
            {
                return $"Error reading file: {ex.Message}";
            }
        }

        public async Task<string> WriteFileAsync(
            [Description("Full path to the file to write")]
            string filePath,
            [Description("Content to write to the file")]
            string content,
            [Description("If true, appends to existing file; otherwise overwrites")]
            bool append = false,
            [Description("If true, opens the file after writing")]
            bool openAfterWrite = false,
            [Description("If true, returns a diff preview without writing")]
            bool previewOnly = false)
        {
            try
            {
                Console.WriteLine($"FileAgent: Writing to file {filePath} (append: {append})");

                if (!IsSafeFilePath(filePath))
                {
                    return "Error: invalid file path; the operation was refused for security.";
                }

                var fileInfo = new FileInfo(filePath);

                if (!_allowedExtensions.Contains(fileInfo.Extension))
                {
                    return $"Error: writing '{fileInfo.Extension}' files is not supported.";
                }

                if (previewOnly)
                {
                    var original = File.Exists(filePath)
                        ? await File.ReadAllTextAsync(filePath)
                        : string.Empty;
                    var proposed = append && File.Exists(filePath)
                        ? original + content
                        : content;

                    return $"Preview only: {filePath}{Environment.NewLine}{FormatDiffPreview(filePath, original, proposed)}";
                }

                if (fileInfo.Directory != null && !fileInfo.Directory.Exists)
                {
                    fileInfo.Directory.Create();
                }

                if (append && File.Exists(filePath))
                {
                    await File.AppendAllTextAsync(filePath, content);
                }
                else
                {
                    await File.WriteAllTextAsync(filePath, content);
                }

                string result = $"File written: {filePath}";

                if (openAfterWrite)
                {
                    result += $"\n{await OpenFileAsync(filePath)}";
                }

                return result;
            }
            catch (UnauthorizedAccessException)
            {
                return $"Error: no permission to write file '{filePath}'.";
            }
            catch (Exception ex)
            {
                return $"Error writing file: {ex.Message}";
            }
        }

        public async Task<string> DeleteFileAsync(
            [Description("Full path to the file to delete")]
            string filePath)
        {
            try
            {
                Console.WriteLine($"FileAgent: Deleting file {filePath}");

                if (!IsSafeFilePath(filePath))
                {
                    return "Error: invalid file path; the operation was refused for security.";
                }

                if (!File.Exists(filePath))
                {
                    return $"Error: file '{filePath}' not found.";
                }

                File.Delete(filePath);
                await Task.CompletedTask; // Maintain async signature
                return $"File deleted: {filePath}";
            }
            catch (UnauthorizedAccessException)
            {
                return $"Error: no permission to delete file '{filePath}'.";
            }
            catch (Exception ex)
            {
                return $"Error deleting file: {ex.Message}";
            }
        }

        public async Task<string> CopyFileAsync(
            [Description("Source file path")]
            string sourcePath,
            [Description("Destination file path")]
            string destinationPath,
            [Description("If true, overwrites existing file")]
            bool overwrite = false,
            [Description("If true, opens the destination folder after copy")]
            bool showInFolder = false)
        {
            try
            {
                Console.WriteLine($"FileAgent: Copying {sourcePath} to {destinationPath}");

                if (!IsSafeFilePath(sourcePath) || !IsSafeFilePath(destinationPath))
                {
                    return "Error: invalid file path; the operation was refused for security.";
                }

                if (!File.Exists(sourcePath))
                {
                    return $"Error: source file '{sourcePath}' not found.";
                }

                if (File.Exists(destinationPath) && !overwrite)
                {
                    return $"Error: destination file '{destinationPath}' already exists. Use overwrite=true.";
                }

                var destInfo = new FileInfo(destinationPath);
                if (destInfo.Directory != null && !destInfo.Directory.Exists)
                {
                    destInfo.Directory.Create();
                }

                File.Copy(sourcePath, destinationPath, overwrite);

                string result = $"File copied: {sourcePath} -> {destinationPath}";

                if (showInFolder)
                {
                    result += $"\n{await ShowInExplorerAsync(destinationPath)}";
                }

                return result;
            }
            catch (Exception ex)
            {
                return $"Error copying file: {ex.Message}";
            }
        }

        public async Task<string> MoveFileAsync(
            [Description("Source file path")]
            string sourcePath,
            [Description("Destination file path")]
            string destinationPath,
            [Description("If true, overwrites existing file")]
            bool overwrite = false)
        {
            try
            {
                Console.WriteLine($"FileAgent: Moving {sourcePath} to {destinationPath}");

                if (!IsSafeFilePath(sourcePath) || !IsSafeFilePath(destinationPath))
                {
                    return "Error: invalid file path; the operation was refused for security.";
                }

                if (!File.Exists(sourcePath))
                {
                    return $"Error: source file '{sourcePath}' not found.";
                }

                if (File.Exists(destinationPath) && !overwrite)
                {
                    return $"Error: destination file '{destinationPath}' already exists. Use overwrite=true.";
                }

                var destInfo = new FileInfo(destinationPath);
                if (destInfo.Directory != null && !destInfo.Directory.Exists)
                {
                    destInfo.Directory.Create();
                }

                File.Move(sourcePath, destinationPath, overwrite);
                await Task.CompletedTask;

                return $"File moved: {sourcePath} -> {destinationPath}";
            }
            catch (Exception ex)
            {
                return $"Error moving file: {ex.Message}";
            }
        }

        public Task<string> FileExistsAsync(
            [Description("Full path to check")]
            string filePath)
        {
            try
            {
                if (!IsSafeFilePath(filePath))
                {
                    return Task.FromResult("Error: invalid file path; the operation was refused for security.");
                }

                var exists = File.Exists(filePath);
                return Task.FromResult(exists
                    ? $"File exists: {filePath}"
                    : $"File not found: {filePath}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Error checking file: {ex.Message}");
            }
        }

        public Task<string> GetFileInfoAsync(
            [Description("Full path to the file")]
            string filePath)
        {
            try
            {
                if (!IsSafeFilePath(filePath))
                {
                    return Task.FromResult("Error: invalid file path; the operation was refused for security.");
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult($"Error: file '{filePath}' not found.");
                }

                var fileInfo = new FileInfo(filePath);
                var sb = new StringBuilder();

                sb.AppendLine($"File information:");
                sb.AppendLine($"  Name: {fileInfo.Name}");
                sb.AppendLine($"  Full path: {fileInfo.FullName}");
                sb.AppendLine($"  Size: {FormatFileSize(fileInfo.Length)}");
                sb.AppendLine($"  Extension: {fileInfo.Extension}");
                sb.AppendLine($"  Created: {fileInfo.CreationTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"  Modified: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"  Last accessed: {fileInfo.LastAccessTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"  Read only: {fileInfo.IsReadOnly}");

                return Task.FromResult(sb.ToString());
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Error getting file information: {ex.Message}");
            }
        }

        public Task<string> SearchFilesAsync(
            [Description("Directory to search in")]
            string directoryPath,
            [Description("File name pattern to search for")]
            string searchPattern,
            [Description("If true, searches subdirectories")]
            bool recursive = true)
        {
            try
            {
                if (!IsSafeFilePath(directoryPath))
                {
                    return Task.FromResult("Error: invalid directory path; the operation was refused for security.");
                }

                if (!Directory.Exists(directoryPath))
                {
                    return Task.FromResult($"Error: directory '{directoryPath}' not found.");
                }

                var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var files = Directory.GetFiles(directoryPath, $"*{searchPattern}*", searchOption);

                if (files.Length == 0)
                {
                    return Task.FromResult($"No files found for pattern '{searchPattern}'.");
                }

                var sb = new StringBuilder();
                sb.AppendLine($"Files found ({files.Length}):");

                foreach (var file in files.Take(20))
                {
                    var fileInfo = new FileInfo(file);
                    sb.AppendLine($"  - {fileInfo.FullName}");
                }

                if (files.Length > 20)
                {
                    sb.AppendLine($"\n  ... and {files.Length - 20} more files");
                }

                return Task.FromResult(sb.ToString());
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Error searching files: {ex.Message}");
            }
        }

        public async Task<string> SearchFileContentAsync(
            [Description("Directory to search in")]
            string directoryPath,
            [Description("Text to search for")]
            string query,
            [Description("File glob pattern such as *.md or *.cs")]
            string searchPattern = "*.*",
            [Description("If true, searches subdirectories")]
            bool recursive = true,
            [Description("Maximum number of matches to return")]
            int maxMatches = 50)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    return "Error: search text is required.";
                }

                if (!IsSafeFilePath(directoryPath))
                {
                    return "Error: invalid directory path; the operation was refused for security.";
                }

                if (!Directory.Exists(directoryPath))
                {
                    return $"Error: directory '{directoryPath}' not found.";
                }

                maxMatches = Math.Clamp(maxMatches, 1, 200);
                searchPattern = string.IsNullOrWhiteSpace(searchPattern) ? "*.*" : searchPattern;
                var enumerationOptions = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = recursive,
                    ReturnSpecialDirectories = false
                };

                var matches = new List<string>();

                foreach (var file in Directory.EnumerateFiles(directoryPath, searchPattern, enumerationOptions))
                {
                    var fileInfo = new FileInfo(file);
                    if (!_allowedExtensions.Contains(fileInfo.Extension)
                        || !IsTextSearchableExtension(fileInfo.Extension)
                        || fileInfo.Length > _maxFileSizeBytes)
                    {
                        continue;
                    }

                    string[] lines;
                    try
                    {
                        lines = await File.ReadAllLinesAsync(file);
                    }
                    catch (Exception ex) when (ex is IOException
                        or UnauthorizedAccessException
                        or DecoderFallbackException)
                    {
                        continue;
                    }

                    var lineNumber = 0;
                    foreach (var line in lines)
                    {
                        lineNumber++;
                        if (!line.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        matches.Add($"{fileInfo.FullName}:{lineNumber}: {line.Trim()}");
                        if (matches.Count >= maxMatches)
                        {
                            return FormatContentSearchResult(query, matches, truncated: true);
                        }
                    }
                }

                return matches.Count == 0
                    ? $"No content matches found for '{query}'."
                    : FormatContentSearchResult(query, matches, truncated: false);
            }
            catch (Exception ex)
            {
                return $"Error searching content: {ex.Message}";
            }
        }

        public Task<string> ListDirectoryTreeAsync(
            [Description("Directory to inspect")]
            string directoryPath,
            [Description("Maximum tree depth")]
            int maxDepth = 2,
            [Description("Maximum entries to return")]
            int maxEntries = 200)
        {
            try
            {
                if (!IsSafeFilePath(directoryPath))
                {
                    return Task.FromResult("Error: invalid directory path; the operation was refused for security.");
                }

                if (!Directory.Exists(directoryPath))
                {
                    return Task.FromResult($"Error: directory '{directoryPath}' not found.");
                }

                maxDepth = Math.Clamp(maxDepth, 0, 8);
                maxEntries = Math.Clamp(maxEntries, 1, 1000);

                var root = new DirectoryInfo(directoryPath);
                var sb = new StringBuilder();
                var count = 0;
                sb.AppendLine($"{root.FullName}");
                AppendDirectoryTree(root, depth: 0, maxDepth, maxEntries, sb, ref count);

                if (count >= maxEntries)
                {
                    sb.AppendLine($"... output limited to {maxEntries} entries.");
                }

                return Task.FromResult(sb.ToString());
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Error building directory tree: {ex.Message}");
            }
        }

        public Task<string> DescribeWorkspaceAsync(
            [Description("Directory to inspect")]
            string directoryPath,
            [Description("Maximum tree depth")]
            int maxDepth = 2,
            [Description("Maximum entries to return")]
            int maxEntries = 200)
        {
            try
            {
                if (!IsSafeFilePath(directoryPath))
                {
                    return Task.FromResult("Error: invalid directory path; the operation was refused for security.");
                }

                if (!Directory.Exists(directoryPath))
                {
                    return Task.FromResult($"Error: directory '{directoryPath}' not found.");
                }

                maxDepth = Math.Clamp(maxDepth, 0, 8);
                maxEntries = Math.Clamp(maxEntries, 1, 1000);

                var root = new DirectoryInfo(directoryPath);
                var extensionCounts = GetExtensionCounts(root, maxEntries);
                var sb = new StringBuilder();
                sb.AppendLine($"Workspace Map: {root.FullName}");
                sb.AppendLine("Extension Summary:");

                if (extensionCounts.Count == 0)
                {
                    sb.AppendLine("  - no files found");
                }
                else
                {
                    foreach (var extension in extensionCounts
                        .OrderByDescending(pair => pair.Value)
                        .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .Take(20))
                    {
                        sb.AppendLine($"  - {extension.Key}: {extension.Value}");
                    }
                }

                sb.AppendLine("Tree:");
                var count = 0;
                AppendDirectoryTree(root, depth: 0, maxDepth, maxEntries, sb, ref count);

                if (count >= maxEntries)
                {
                    sb.AppendLine($"... output limited to {maxEntries} entries.");
                }

                return Task.FromResult(sb.ToString());
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Error building workspace map: {ex.Message}");
            }
        }

        public async Task<string> OutlineCodeAsync(
            [Description("Full path to the code file")]
            string filePath,
            [Description("Maximum number of symbols to return")]
            int maxSymbols = 100)
        {
            try
            {
                if (!IsSafeFilePath(filePath))
                {
                    return "Error: invalid file path; the operation was refused for security.";
                }

                if (!File.Exists(filePath))
                {
                    return $"Error: file '{filePath}' not found.";
                }

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > _maxFileSizeBytes)
                {
                    return $"Error: file is too large ({fileInfo.Length / 1024 / 1024}MB). Maximum size: {_maxFileSizeBytes / 1024 / 1024}MB";
                }

                if (!IsCodeOutlineExtension(fileInfo.Extension))
                {
                    return $"Error: code outline is not supported for '{fileInfo.Extension}' files.";
                }

                maxSymbols = Math.Clamp(maxSymbols, 1, 500);
                var lines = await File.ReadAllLinesAsync(filePath);
                var symbols = new List<string>();

                for (var index = 0; index < lines.Length && symbols.Count < maxSymbols; index++)
                {
                    var symbol = TryFormatCodeSymbol(lines[index]);
                    if (symbol is not null)
                    {
                        symbols.Add($"{index + 1}: {symbol}");
                    }
                }

                var sb = new StringBuilder();
                sb.AppendLine($"Code Outline: {fileInfo.FullName}");
                if (symbols.Count == 0)
                {
                    sb.AppendLine("  - no symbols found");
                }
                else
                {
                    foreach (var symbol in symbols)
                    {
                        sb.AppendLine(symbol);
                    }
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error building code outline: {ex.Message}";
            }
        }

        public async Task<string> ReplaceRangeAsync(
            [Description("Full path to the file")]
            string filePath,
            [Description("Starting line number (1-based)")]
            int startLine = 1,
            [Description("Number of lines to replace")]
            int lineCount = 0,
            [Description("Replacement text")]
            string replacement = "",
            [Description("If true, returns the diff without writing the file")]
            bool previewOnly = false)
        {
            try
            {
                var validationError = ValidateEditableTextFile(filePath);
                if (validationError is not null)
                {
                    return validationError;
                }

                var original = await File.ReadAllTextAsync(filePath);
                var newline = DetectNewline(original);
                var lines = SplitNormalizedLines(original);
                if (startLine < 1 || startLine > lines.Length)
                {
                    return $"Error: invalid line number. The file has {lines.Length} lines.";
                }

                if (lineCount < 0 || startLine + lineCount - 1 > lines.Length)
                {
                    return $"Error: invalid line range. The file has {lines.Length} lines.";
                }

                var replacementLines = SplitNormalizedLines(replacement);
                var proposedLines = lines
                    .Take(startLine - 1)
                    .Concat(replacementLines)
                    .Concat(lines.Skip(startLine - 1 + lineCount))
                    .ToArray();
                var proposed = string.Join(newline, proposedLines);
                var diff = FormatDiffPreview(filePath, original, proposed);

                if (!previewOnly)
                {
                    await File.WriteAllTextAsync(filePath, proposed);
                }

                var mode = previewOnly ? "Preview only" : "Patch applied";
                return $"{mode}: {filePath}{Environment.NewLine}{diff}";
            }
            catch (Exception ex)
            {
                return $"Error replacing line range: {ex.Message}";
            }
        }

        public async Task<string> PatchFileAsync(
            [Description("Full path to the file")]
            string filePath,
            [Description("Exact text to replace")]
            string oldText,
            [Description("Replacement text")]
            string newText,
            [Description("Expected number of exact matches")]
            int expectedOccurrences = 1,
            [Description("If true, returns the diff without writing the file")]
            bool previewOnly = false)
        {
            try
            {
                var validationError = ValidateEditableTextFile(filePath);
                if (validationError is not null)
                {
                    return validationError;
                }

                if (string.IsNullOrEmpty(oldText))
                {
                    return "Error: oldText is required.";
                }

                expectedOccurrences = Math.Max(1, expectedOccurrences);
                var original = await File.ReadAllTextAsync(filePath);
                var occurrenceCount = CountOccurrences(original, oldText);
                if (occurrenceCount != expectedOccurrences)
                {
                    return $"Error: expected {expectedOccurrences} matches, found {occurrenceCount}.";
                }

                var proposed = original.Replace(oldText, newText, StringComparison.Ordinal);
                var diff = FormatDiffPreview(filePath, original, proposed);

                if (!previewOnly)
                {
                    await File.WriteAllTextAsync(filePath, proposed);
                }

                var mode = previewOnly ? "Preview only" : "Patch applied";
                return $"{mode}: {filePath}{Environment.NewLine}{diff}";
            }
            catch (Exception ex)
            {
                return $"Error patching file: {ex.Message}";
            }
        }

        public async Task<string> PreviewDiffAsync(
            [Description("Full path to the file")]
            string filePath,
            [Description("Complete proposed file content")]
            string proposedContent)
        {
            try
            {
                var validationError = ValidateReadableTextFile(filePath);
                if (validationError is not null)
                {
                    return validationError;
                }

                var original = await File.ReadAllTextAsync(filePath);
                return FormatDiffPreview(filePath, original, proposedContent);
            }
            catch (Exception ex)
            {
                return $"Error building diff preview: {ex.Message}";
            }
        }

        public async Task<string> GetGitDiffSummaryAsync()
        {
            var status = await RunReadOnlyGitAsync(["status", "--short", "--branch"]);
            var unstaged = await RunReadOnlyGitAsync(["diff", "--stat", "--"]);
            var staged = await RunReadOnlyGitAsync(["diff", "--cached", "--stat", "--"]);

            var sb = new StringBuilder()
                .AppendLine($"Git Snapshot: {_defaultWorkingDirectory}")
                .AppendLine("Status:")
                .AppendLine(string.IsNullOrWhiteSpace(status.Output) ? "  (clean or unavailable)" : Indent(status.Output))
                .AppendLine("Unstaged diff:")
                .AppendLine(string.IsNullOrWhiteSpace(unstaged.Output) ? "  (none)" : Indent(unstaged.Output))
                .AppendLine("Staged diff:")
                .AppendLine(string.IsNullOrWhiteSpace(staged.Output) ? "  (none)" : Indent(staged.Output));

            if (!status.Success || !unstaged.Success || !staged.Success)
            {
                sb.AppendLine("Warnings:");
                AppendGitWarning(sb, status);
                AppendGitWarning(sb, unstaged);
                AppendGitWarning(sb, staged);
            }

            return sb.ToString().TrimEnd();
        }

        public async Task<string> CreateDirectoryAsync(
            [Description("Full path of the directory to create")]
            string directoryPath,
            [Description("If true, opens the folder in Explorer after creation")]
            bool openAfterCreation = false)
        {
            try
            {
                Console.WriteLine($"FileAgent: Creating directory {directoryPath}");

                if (!IsSafeFilePath(directoryPath))
                {
                    return "Error: invalid directory path; the operation was refused for security.";
                }

                if (Directory.Exists(directoryPath))
                {
                    return $"Directory already exists: {directoryPath}";
                }

                Directory.CreateDirectory(directoryPath);

                string result = $"Directory created: {directoryPath}";

                if (openAfterCreation)
                {
                    result += $"\n{await OpenDirectoryAsync(directoryPath)}";
                }

                return result;
            }
            catch (Exception ex)
            {
                return $"Error creating directory: {ex.Message}";
            }
        }

        public async Task<string> ReadLinesAsync(
            [Description("Full path to the file")]
            string filePath,
            [Description("Starting line number (1-based)")]
            int startLine = 1,
            [Description("Number of lines to read (0 for all remaining)")]
            int lineCount = 0)
        {
            try
            {
                var validationError = ValidateReadableTextFile(filePath);
                if (validationError is not null)
                {
                    return validationError;
                }

                var allLines = await File.ReadAllLinesAsync(filePath);

                if (startLine < 1 || startLine > allLines.Length)
                {
                    return $"Error: invalid line number. The file has {allLines.Length} lines.";
                }

                var linesToRead = lineCount > 0
                    ? Math.Min(lineCount, allLines.Length - startLine + 1)
                    : allLines.Length - startLine + 1;

                var selectedLines = allLines
                    .Skip(startLine - 1)
                    .Take(linesToRead)
                    .ToArray();

                var sb = new StringBuilder();
                sb.AppendLine($"File: {filePath}");
                sb.AppendLine($"Lines {startLine}-{startLine + selectedLines.Length - 1}:");
                sb.AppendLine(new string('-', 50));

                for (int i = 0; i < selectedLines.Length; i++)
                {
                    sb.AppendLine($"{startLine + i}: {selectedLines[i]}");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error reading lines: {ex.Message}";
            }
        }

        #endregion

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        private static bool IsTextSearchableExtension(string extension)
        {
            return extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".log", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".css", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".py", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".java", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".cpp", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".h", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".yml", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".ini", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".svg", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCodeOutlineExtension(string extension)
        {
            return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".py", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".java", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".cpp", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".h", StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, int> GetExtensionCounts(DirectoryInfo root, int maxEntries)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var enumerationOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                ReturnSpecialDirectories = false
            };

            var inspected = 0;
            foreach (var file in root.EnumerateFiles("*", enumerationOptions))
            {
                if (inspected >= maxEntries)
                {
                    break;
                }

                inspected++;
                var extension = string.IsNullOrWhiteSpace(file.Extension)
                    ? "[no extension]"
                    : file.Extension;
                counts[extension] = counts.TryGetValue(extension, out var existing)
                    ? existing + 1
                    : 1;
            }

            return counts;
        }

        private static string? TryFormatCodeSymbol(string line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                return null;
            }

            if (Regex.IsMatch(trimmed, @"^(public|private|protected|internal|static|sealed|abstract|partial|export|class|interface|record|struct|enum)\b.*\b(class|interface|record|struct|enum)\b\s+\w+", RegexOptions.IgnoreCase)
                || Regex.IsMatch(trimmed, @"^(public|private|protected|internal|static|async|override|virtual|sealed|partial)\b.*\w+\s+\w+\s*\([^;]*\)\s*(\{|=>)?$", RegexOptions.IgnoreCase)
                || Regex.IsMatch(trimmed, @"^(export\s+)?(async\s+)?function\s+\w+\s*\(", RegexOptions.IgnoreCase)
                || Regex.IsMatch(trimmed, @"^(export\s+)?const\s+\w+\s*=\s*(async\s*)?\([^)]*\)\s*=>", RegexOptions.IgnoreCase)
                || Regex.IsMatch(trimmed, @"^(class|def)\s+\w+", RegexOptions.IgnoreCase))
            {
                return trimmed.TrimEnd('{').TrimEnd(';').Trim();
            }

            return null;
        }

        private string? ValidateReadableTextFile(string filePath)
        {
            if (!IsSafeFilePath(filePath))
            {
                return "Error: invalid file path; the operation was refused for security.";
            }

            if (!File.Exists(filePath))
            {
                return $"Error: file '{filePath}' not found.";
            }

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > _maxFileSizeBytes)
            {
                return $"Error: file is too large ({fileInfo.Length / 1024 / 1024}MB). Maximum size: {_maxFileSizeBytes / 1024 / 1024}MB";
            }

            if (!_allowedExtensions.Contains(fileInfo.Extension) || !IsTextSearchableExtension(fileInfo.Extension))
            {
                return $"Error: '{fileInfo.Extension}' files are not supported for text editing.";
            }

            return null;
        }

        private string? ValidateEditableTextFile(string filePath)
        {
            var readableValidationError = ValidateReadableTextFile(filePath);
            if (readableValidationError is not null)
            {
                return readableValidationError;
            }

            var fileInfo = new FileInfo(filePath);
            return fileInfo.IsReadOnly
                ? $"Error: file '{filePath}' is read-only."
                : null;
        }

        private bool IsSafeFilePath(string path)
        {
            return _restrictToDefaultWorkingDirectory
                ? SecurityUtilities.IsSafeFilePath(path, _defaultWorkingDirectory)
                : SecurityUtilities.IsSafeFilePath(path);
        }

        private static string FormatDiffPreview(string filePath, string original, string proposed)
        {
            var originalLines = SplitNormalizedLines(original);
            var proposedLines = SplitNormalizedLines(proposed);
            var max = Math.Max(originalLines.Length, proposedLines.Length);
            var changed = false;
            var sb = new StringBuilder();
            sb.AppendLine($"Diff Preview: {filePath}");
            sb.AppendLine("--- current");
            sb.AppendLine("+++ proposed");

            var emitted = 0;
            for (var index = 0; index < max; index++)
            {
                if (emitted >= MaxDiffPreviewLines)
                {
                    sb.AppendLine($"... diff preview truncated at {MaxDiffPreviewLines} lines.");
                    break;
                }

                var hasOriginal = index < originalLines.Length;
                var hasProposed = index < proposedLines.Length;
                var originalLine = hasOriginal ? originalLines[index] : string.Empty;
                var proposedLine = hasProposed ? proposedLines[index] : string.Empty;

                if (hasOriginal && hasProposed && originalLine == proposedLine)
                {
                    sb.AppendLine($" {originalLine}");
                    emitted++;
                    continue;
                }

                changed = true;
                if (hasOriginal)
                {
                    sb.AppendLine($"-{originalLine}");
                    emitted++;
                }

                if (hasProposed && emitted < MaxDiffPreviewLines)
                {
                    sb.AppendLine($"+{proposedLine}");
                    emitted++;
                }
            }

            if (!changed)
            {
                sb.AppendLine("No changes.");
            }

            return sb.ToString();
        }

        private static string DetectNewline(string content)
        {
            return content.Contains("\r\n", StringComparison.Ordinal)
                ? "\r\n"
                : "\n";
        }

        private static string[] SplitNormalizedLines(string content)
        {
            if (content.Length == 0)
            {
                return [];
            }

            return content
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Split('\n');
        }

        private static int CountOccurrences(string content, string search)
        {
            var count = 0;
            var index = 0;
            while (index < content.Length)
            {
                var next = content.IndexOf(search, index, StringComparison.Ordinal);
                if (next < 0)
                {
                    break;
                }

                count++;
                index = next + search.Length;
            }

            return count;
        }

        private static string FormatContentSearchResult(
            string query,
            IReadOnlyCollection<string> matches,
            bool truncated)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Content matches: '{query}' ({matches.Count})");
            foreach (var match in matches)
            {
                sb.AppendLine($"  - {match}");
            }

            if (truncated)
            {
                sb.AppendLine("... results truncated.");
            }

            return sb.ToString();
        }

        private async Task<GitCommandResult> RunReadOnlyGitAsync(IReadOnlyList<string> arguments)
        {
            try
            {
                if (!Directory.Exists(_defaultWorkingDirectory))
                {
                    return new GitCommandResult(false, string.Empty, "Workspace directory does not exist.");
                }

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        WorkingDirectory = _defaultWorkingDirectory,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                foreach (var argument in arguments)
                {
                    process.StartInfo.ArgumentList.Add(argument);
                }

                if (!process.Start())
                {
                    return new GitCommandResult(false, string.Empty, "Git process could not be started.");
                }

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                var waitTask = process.WaitForExitAsync();
                var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(8)));
                if (completed != waitTask)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    return new GitCommandResult(false, string.Empty, "Git command timed out.");
                }

                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                return new GitCommandResult(process.ExitCode == 0, stdout.Trim(), stderr.Trim());
            }
            catch (Exception ex) when (ex is Win32Exception
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
            {
                return new GitCommandResult(false, string.Empty, ex.Message);
            }
        }

        private static void AppendGitWarning(StringBuilder builder, GitCommandResult result)
        {
            if (result.Success || string.IsNullOrWhiteSpace(result.Error))
            {
                return;
            }

            builder.AppendLine($"  - {result.Error}");
        }

        private static string Indent(string value)
        {
            return string.Join(
                Environment.NewLine,
                value
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n')
                    .Split('\n')
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => $"  {line.TrimEnd()}"));
        }

        private readonly record struct GitCommandResult(
            bool Success,
            string Output,
            string Error);

        private static void AppendDirectoryTree(
            DirectoryInfo directory,
            int depth,
            int maxDepth,
            int maxEntries,
            StringBuilder builder,
            ref int count)
        {
            if (depth >= maxDepth || count >= maxEntries)
            {
                return;
            }

            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = directory
                    .EnumerateFileSystemInfos()
                    .OrderBy(entry => entry is FileInfo)
                    .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                builder.AppendLine($"{new string(' ', (depth + 1) * 2)}[access denied]");
                return;
            }

            foreach (var entry in entries)
            {
                if (count >= maxEntries)
                {
                    return;
                }

                count++;
                var marker = entry is DirectoryInfo ? "[D]" : "[F]";
                builder.AppendLine($"{new string(' ', (depth + 1) * 2)}{marker} {entry.Name}");

                if (entry is DirectoryInfo childDirectory)
                {
                    AppendDirectoryTree(childDirectory, depth + 1, maxDepth, maxEntries, builder, ref count);
                }
            }
        }
    }
}
