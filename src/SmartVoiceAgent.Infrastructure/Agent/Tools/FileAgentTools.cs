using AgentFrameworkToolkit.Tools;
using Microsoft.Extensions.AI;
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
        private readonly HashSet<string> _allowedExtensions;
        private readonly HashSet<string> _executableExtensions;
        private readonly long _maxFileSizeBytes;

        public FileAgentTools(
            string defaultWorkingDirectory = null,
            long maxFileSizeBytes = 10 * 1024 * 1024) // 10MB default
        {
            _defaultWorkingDirectory = defaultWorkingDirectory ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
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

        [AITool("create_file", "Creates a new file with optional initial content and can optionally open it.")]
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
                if (!SecurityUtilities.IsSafeFilePath(filePath, _defaultWorkingDirectory))
                {
                    return "Hata: Geçersiz dosya yolu. Güvenlik nedeniyle işlem reddedildi.";
                }

                if (File.Exists(filePath))
                {
                    return $"Hata: '{filePath}' dosyası zaten mevcut.";
                }

                var fileInfo = new FileInfo(filePath);

                // Check file extension
                if (!_allowedExtensions.Contains(fileInfo.Extension))
                {
                    return $"Hata: '{fileInfo.Extension}' uzantılı dosyalar desteklenmiyor.";
                }

                // Create directory if it doesn't exist
                if (fileInfo.Directory != null && !fileInfo.Directory.Exists)
                {
                    fileInfo.Directory.Create();
                }

                await File.WriteAllTextAsync(filePath, content);

                string result = $"Dosya başarıyla oluşturuldu: {filePath}";

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
                return $"Dosya oluşturma hatası: {ex.Message}";
            }
        }

        [AITool("list_files", "Lists files in a directory with optional filter and can open the folder.")]
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
                if (!Directory.Exists(directoryPath))
                {
                    return $"Hata: '{directoryPath}' dizini bulunamadı.";
                }

                var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var files = Directory.GetFiles(directoryPath, searchPattern, searchOption);

                var sb = new StringBuilder();

                if (files.Length == 0)
                {
                    sb.AppendLine($"'{directoryPath}' dizininde dosya bulunamadı.");
                }
                else
                {
                    sb.AppendLine($"Dizindeki dosyalar ({files.Length} adet):");
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
                    sb.AppendLine($"\nKlasör açıldı: {openResult}");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Dosya listeleme hatası: {ex.Message}";
            }
        }

        #endregion

        #region New File Opening Tools

        [AITool("open_file", "Opens a file with its default application (Notepad, Word, etc.).")]
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
                if (!SecurityUtilities.IsSafeFilePath(filePath, _defaultWorkingDirectory))
                {
                    return "Güvenlik hatası: Dosya yolu güvenli değil. Yol dışarı çıkma veya geçersiz karakterler içeriyor.";
                }

                if (!File.Exists(filePath))
                {
                    return $"Hata: '{filePath}' dosyası bulunamadı.";
                }

                var fileInfo = new FileInfo(filePath);
                var ext = fileInfo.Extension;

                // Security check for executables
                if (_executableExtensions.Contains(ext) && !allowExecutables)
                {
                    return $"Güvenlik uyarısı: '{ext}' dosyaları otomatik olarak açılamaz. Lütfen manuel olarak çalıştırın.";
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

                return $"Dosya açıldı: {fileInfo.Name} ({ext})";
            }
            catch (Exception ex)
            {
                return $"Dosya açılma hatası: {ex.Message}";
            }
        }

        [AITool("open_directory", "Opens a folder in File Explorer.")]
        public async Task<string> OpenDirectoryAsync(
            [Description("Full path to the directory to open")]
            string directoryPath,
            [Description("If true, selects a specific file in that folder (highlights it)")]
            string selectFile = null)
        {
            try
            {
                Console.WriteLine($"FileAgent: Opening directory {directoryPath}");

                // Security: Validate path to prevent path traversal
                if (!SecurityUtilities.IsSafeFilePath(directoryPath, _defaultWorkingDirectory))
                {
                    return "Güvenlik hatası: Dizin yolu güvenli değil. Yol dışarı çıkma veya geçersiz karakterler içeriyor.";
                }

                if (!Directory.Exists(directoryPath))
                {
                    return $"Hata: '{directoryPath}' dizini bulunamadı.";
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

                return $"Klasör açıldı: {directoryPath}";
            }
            catch (Exception ex)
            {
                return $"Klasör açılma hatası: {ex.Message}";
            }
        }

        [AITool("show_in_explorer", "Shows a specific file or folder in File Explorer with highlighting.")]
        public async Task<string> ShowInExplorerAsync(
            [Description("Full path to the file or folder to show")]
            string path)
        {
            try
            {
                // Security: Validate path to prevent path traversal
                if (!SecurityUtilities.IsSafeFilePath(path, _defaultWorkingDirectory))
                {
                    return "Güvenlik hatası: Yol güvenli değil. Yol dışarı çıkma veya geçersiz karakterler içeriyor.";
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
                    return $"Dosya konumu gösterildi: {Path.GetFileName(path)}";
                }
                else if (Directory.Exists(path))
                {
                    return await OpenDirectoryAsync(path);
                }
                else
                {
                    return $"Hata: '{path}' bulunamadı.";
                }
            }
            catch (Exception ex)
            {
                return $"Explorer hatası: {ex.Message}";
            }
        }

        #endregion

        #region Original Methods (Keep existing implementations)

        [AITool("read_file", "Reads the content of a file from the file system.")]
        public async Task<string> ReadFileAsync(
            [Description("Full path to the file to read")]
            string filePath)
        {
            try
            {
                Console.WriteLine($"FileAgent: Reading file {filePath}");

                if (!File.Exists(filePath))
                {
                    return $"Hata: '{filePath}' dosyası bulunamadı.";
                }

                var fileInfo = new FileInfo(filePath);

                if (fileInfo.Length > _maxFileSizeBytes)
                {
                    return $"Hata: Dosya çok büyük ({fileInfo.Length / 1024 / 1024}MB). Maksimum boyut: {_maxFileSizeBytes / 1024 / 1024}MB";
                }

                if (!_allowedExtensions.Contains(fileInfo.Extension))
                {
                    return $"Hata: '{fileInfo.Extension}' uzantılı dosyalar desteklenmiyor.";
                }

                var content = await File.ReadAllTextAsync(filePath);
                return $"Dosya başarıyla okundu:\n\n{content}";
            }
            catch (UnauthorizedAccessException)
            {
                return $"Hata: '{filePath}' dosyasına erişim izni yok.";
            }
            catch (Exception ex)
            {
                return $"Dosya okuma hatası: {ex.Message}";
            }
        }

        [AITool("write_file", "Writes content to a file. Creates the file if it doesn't exist.")]
        public async Task<string> WriteFileAsync(
            [Description("Full path to the file to write")]
            string filePath,
            [Description("Content to write to the file")]
            string content,
            [Description("If true, appends to existing file; otherwise overwrites")]
            bool append = false,
            [Description("If true, opens the file after writing")]
            bool openAfterWrite = false)
        {
            try
            {
                Console.WriteLine($"FileAgent: Writing to file {filePath} (append: {append})");

                var fileInfo = new FileInfo(filePath);

                if (!_allowedExtensions.Contains(fileInfo.Extension))
                {
                    return $"Hata: '{fileInfo.Extension}' uzantılı dosyalara yazma desteklenmiyor.";
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

                string result = $"Dosya başarıyla yazıldı: {filePath}";

                if (openAfterWrite)
                {
                    result += $"\n{await OpenFileAsync(filePath)}";
                }

                return result;
            }
            catch (UnauthorizedAccessException)
            {
                return $"Hata: '{filePath}' dosyasına yazma izni yok.";
            }
            catch (Exception ex)
            {
                return $"Dosya yazma hatası: {ex.Message}";
            }
        }

        [AITool("delete_file", "Deletes a file from the file system.")]
        public async Task<string> DeleteFileAsync(
            [Description("Full path to the file to delete")]
            string filePath)
        {
            try
            {
                Console.WriteLine($"FileAgent: Deleting file {filePath}");

                if (!File.Exists(filePath))
                {
                    return $"Hata: '{filePath}' dosyası bulunamadı.";
                }

                File.Delete(filePath);
                await Task.CompletedTask; // Maintain async signature
                return $"Dosya başarıyla silindi: {filePath}";
            }
            catch (UnauthorizedAccessException)
            {
                return $"Hata: '{filePath}' dosyasını silme izni yok.";
            }
            catch (Exception ex)
            {
                return $"Dosya silme hatası: {ex.Message}";
            }
        }

        [AITool("copy_file", "Copies a file to a new location.")]
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

                if (!File.Exists(sourcePath))
                {
                    return $"Hata: Kaynak dosya '{sourcePath}' bulunamadı.";
                }

                if (File.Exists(destinationPath) && !overwrite)
                {
                    return $"Hata: Hedef dosya '{destinationPath}' zaten mevcut. overwrite=true kullanın.";
                }

                var destInfo = new FileInfo(destinationPath);
                if (destInfo.Directory != null && !destInfo.Directory.Exists)
                {
                    destInfo.Directory.Create();
                }

                File.Copy(sourcePath, destinationPath, overwrite);

                string result = $"Dosya başarıyla kopyalandı: {sourcePath} -> {destinationPath}";

                if (showInFolder)
                {
                    result += $"\n{await ShowInExplorerAsync(destinationPath)}";
                }

                return result;
            }
            catch (Exception ex)
            {
                return $"Dosya kopyalama hatası: {ex.Message}";
            }
        }

        [AITool("move_file", "Moves a file to a new location.")]
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

                if (!File.Exists(sourcePath))
                {
                    return $"Hata: Kaynak dosya '{sourcePath}' bulunamadı.";
                }

                if (File.Exists(destinationPath) && !overwrite)
                {
                    return $"Hata: Hedef dosya '{destinationPath}' zaten mevcut. overwrite=true kullanın.";
                }

                var destInfo = new FileInfo(destinationPath);
                if (destInfo.Directory != null && !destInfo.Directory.Exists)
                {
                    destInfo.Directory.Create();
                }

                File.Move(sourcePath, destinationPath, overwrite);
                await Task.CompletedTask;

                return $"Dosya başarıyla taşındı: {sourcePath} -> {destinationPath}";
            }
            catch (Exception ex)
            {
                return $"Dosya taşıma hatası: {ex.Message}";
            }
        }

        [AITool("file_exists", "Checks if a file exists at the specified path.")]
        public Task<string> FileExistsAsync(
            [Description("Full path to check")]
            string filePath)
        {
            try
            {
                var exists = File.Exists(filePath);
                return Task.FromResult(exists
                    ? $"Dosya mevcut: {filePath}"
                    : $"Dosya bulunamadı: {filePath}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Kontrol hatası: {ex.Message}");
            }
        }

        [AITool("get_file_info", "Gets detailed information about a file.")]
        public Task<string> GetFileInfoAsync(
            [Description("Full path to the file")]
            string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return Task.FromResult($"Hata: '{filePath}' dosyası bulunamadı.");
                }

                var fileInfo = new FileInfo(filePath);
                var sb = new StringBuilder();

                sb.AppendLine($"Dosya Bilgileri:");
                sb.AppendLine($"  İsim: {fileInfo.Name}");
                sb.AppendLine($"  Tam Yol: {fileInfo.FullName}");
                sb.AppendLine($"  Boyut: {FormatFileSize(fileInfo.Length)}");
                sb.AppendLine($"  Uzantı: {fileInfo.Extension}");
                sb.AppendLine($"  Oluşturma: {fileInfo.CreationTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"  Son Değişiklik: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"  Son Erişim: {fileInfo.LastAccessTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"  Salt Okunur: {fileInfo.IsReadOnly}");

                return Task.FromResult(sb.ToString());
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Dosya bilgisi alma hatası: {ex.Message}");
            }
        }

        [AITool("search_files", "Searches for files by name pattern in a directory.")]
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
                if (!Directory.Exists(directoryPath))
                {
                    return Task.FromResult($"Hata: '{directoryPath}' dizini bulunamadı.");
                }

                var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var files = Directory.GetFiles(directoryPath, $"*{searchPattern}*", searchOption);

                if (files.Length == 0)
                {
                    return Task.FromResult($"'{searchPattern}' deseni için dosya bulunamadı.");
                }

                var sb = new StringBuilder();
                sb.AppendLine($"Bulunan dosyalar ({files.Length} adet):");

                foreach (var file in files.Take(20))
                {
                    var fileInfo = new FileInfo(file);
                    sb.AppendLine($"  - {fileInfo.FullName}");
                }

                if (files.Length > 20)
                {
                    sb.AppendLine($"\n  ... ve {files.Length - 20} dosya daha");
                }

                return Task.FromResult(sb.ToString());
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Dosya arama hatası: {ex.Message}");
            }
        }

        [AITool("search_file_content", "Searches text file contents and returns bounded path/line snippets.")]
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
                    return "Hata: Arama metni gerekli.";
                }

                if (!Directory.Exists(directoryPath))
                {
                    return $"Hata: '{directoryPath}' dizini bulunamadı.";
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
                    ? $"'{query}' için içerik eşleşmesi bulunamadı."
                    : FormatContentSearchResult(query, matches, truncated: false);
            }
            catch (Exception ex)
            {
                return $"İçerik arama hatası: {ex.Message}";
            }
        }

        [AITool("list_directory_tree", "Returns a bounded directory tree for project or folder inspection.")]
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
                if (!Directory.Exists(directoryPath))
                {
                    return Task.FromResult($"Hata: '{directoryPath}' dizini bulunamadı.");
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
                    sb.AppendLine($"... çıktı {maxEntries} kayıt ile sınırlandı.");
                }

                return Task.FromResult(sb.ToString());
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Dizin ağacı hatası: {ex.Message}");
            }
        }

        [AITool("describe_workspace", "Returns a bounded workspace map with directory tree and file extension summary.")]
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
                if (!Directory.Exists(directoryPath))
                {
                    return Task.FromResult($"Hata: '{directoryPath}' dizini bulunamadı.");
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
                    sb.AppendLine($"... çıktı {maxEntries} kayıt ile sınırlandı.");
                }

                return Task.FromResult(sb.ToString());
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Workspace map hatası: {ex.Message}");
            }
        }

        [AITool("code_outline", "Returns a lightweight line-numbered outline for common code files.")]
        public async Task<string> OutlineCodeAsync(
            [Description("Full path to the code file")]
            string filePath,
            [Description("Maximum number of symbols to return")]
            int maxSymbols = 100)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return $"Hata: '{filePath}' dosyası bulunamadı.";
                }

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > _maxFileSizeBytes)
                {
                    return $"Hata: Dosya çok büyük ({fileInfo.Length / 1024 / 1024}MB). Maksimum boyut: {_maxFileSizeBytes / 1024 / 1024}MB";
                }

                if (!IsCodeOutlineExtension(fileInfo.Extension))
                {
                    return $"Hata: '{fileInfo.Extension}' uzantısı için code outline desteklenmiyor.";
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
                return $"Code outline hatası: {ex.Message}";
            }
        }

        [AITool("replace_file_range", "Replaces a 1-based line range in a text file and returns a diff preview.")]
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
                    return $"Hata: Geçersiz satır numarası. Dosyada {lines.Length} satır var.";
                }

                if (lineCount < 0 || startLine + lineCount - 1 > lines.Length)
                {
                    return $"Hata: Geçersiz satır aralığı. Dosyada {lines.Length} satır var.";
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
                return $"Dosya aralığı değiştirme hatası: {ex.Message}";
            }
        }

        [AITool("patch_file", "Safely replaces exact text in a file and returns a diff preview.")]
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
                    return "Hata: oldText değeri gerekli.";
                }

                expectedOccurrences = Math.Max(1, expectedOccurrences);
                var original = await File.ReadAllTextAsync(filePath);
                var occurrenceCount = CountOccurrences(original, oldText);
                if (occurrenceCount != expectedOccurrences)
                {
                    return $"Hata: Beklenen eşleşme sayısı {expectedOccurrences}, bulunan {occurrenceCount}.";
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
                return $"Dosya patch hatası: {ex.Message}";
            }
        }

        [AITool("preview_file_diff", "Returns a diff between the current file and proposed complete content without writing.")]
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
                return $"Diff preview hatası: {ex.Message}";
            }
        }

        [AITool("create_directory", "Creates a new directory.")]
        public async Task<string> CreateDirectoryAsync(
            [Description("Full path of the directory to create")]
            string directoryPath,
            [Description("If true, opens the folder in Explorer after creation")]
            bool openAfterCreation = false)
        {
            try
            {
                Console.WriteLine($"FileAgent: Creating directory {directoryPath}");

                if (Directory.Exists(directoryPath))
                {
                    return $"Dizin zaten mevcut: {directoryPath}";
                }

                Directory.CreateDirectory(directoryPath);

                string result = $"Dizin başarıyla oluşturuldu: {directoryPath}";

                if (openAfterCreation)
                {
                    result += $"\n{await OpenDirectoryAsync(directoryPath)}";
                }

                return result;
            }
            catch (Exception ex)
            {
                return $"Dizin oluşturma hatası: {ex.Message}";
            }
        }

        [AITool("read_lines", "Reads specific lines from a file.")]
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
                if (!File.Exists(filePath))
                {
                    return $"Hata: '{filePath}' dosyası bulunamadı.";
                }

                var allLines = await File.ReadAllLinesAsync(filePath);

                if (startLine < 1 || startLine > allLines.Length)
                {
                    return $"Hata: Geçersiz satır numarası. Dosyada {allLines.Length} satır var.";
                }

                var linesToRead = lineCount > 0
                    ? Math.Min(lineCount, allLines.Length - startLine + 1)
                    : allLines.Length - startLine + 1;

                var selectedLines = allLines
                    .Skip(startLine - 1)
                    .Take(linesToRead)
                    .ToArray();

                var sb = new StringBuilder();
                sb.AppendLine($"Dosya: {filePath}");
                sb.AppendLine($"Satırlar {startLine}-{startLine + selectedLines.Length - 1}:");
                sb.AppendLine(new string('-', 50));

                for (int i = 0; i < selectedLines.Length; i++)
                {
                    sb.AppendLine($"{startLine + i}: {selectedLines[i]}");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Satır okuma hatası: {ex.Message}";
            }
        }

        #endregion

        public IEnumerable<AIFunction> GetTools()
        {
            return
            [
                // File Operations (Enhanced)
                AIFunctionFactory.Create(ReadFileAsync),
                AIFunctionFactory.Create(WriteFileAsync),
                AIFunctionFactory.Create(CreateFileAsync),
                AIFunctionFactory.Create(DeleteFileAsync),
                AIFunctionFactory.Create(CopyFileAsync),
                AIFunctionFactory.Create(MoveFileAsync),
                AIFunctionFactory.Create(FileExistsAsync),
                AIFunctionFactory.Create(GetFileInfoAsync),
                AIFunctionFactory.Create(ListFilesAsync),
                AIFunctionFactory.Create(SearchFilesAsync),
                AIFunctionFactory.Create(SearchFileContentAsync),
                AIFunctionFactory.Create(ListDirectoryTreeAsync),
                AIFunctionFactory.Create(DescribeWorkspaceAsync),
                AIFunctionFactory.Create(OutlineCodeAsync),
                AIFunctionFactory.Create(ReplaceRangeAsync),
                AIFunctionFactory.Create(PatchFileAsync),
                AIFunctionFactory.Create(PreviewDiffAsync),
                AIFunctionFactory.Create(CreateDirectoryAsync),
                AIFunctionFactory.Create(ReadLinesAsync),
                
                // NEW: File Opening Tools
                AIFunctionFactory.Create(OpenFileAsync),
                AIFunctionFactory.Create(OpenDirectoryAsync),
                AIFunctionFactory.Create(ShowInExplorerAsync)
            ];
        }

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
            if (!SecurityUtilities.IsSafeFilePath(filePath, _defaultWorkingDirectory))
            {
                return "Hata: Geçersiz dosya yolu. Güvenlik nedeniyle işlem reddedildi.";
            }

            if (!File.Exists(filePath))
            {
                return $"Hata: '{filePath}' dosyası bulunamadı.";
            }

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > _maxFileSizeBytes)
            {
                return $"Hata: Dosya çok büyük ({fileInfo.Length / 1024 / 1024}MB). Maksimum boyut: {_maxFileSizeBytes / 1024 / 1024}MB";
            }

            if (!_allowedExtensions.Contains(fileInfo.Extension) || !IsTextSearchableExtension(fileInfo.Extension))
            {
                return $"Hata: '{fileInfo.Extension}' uzantılı dosyalar metin düzenleme için desteklenmiyor.";
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
                ? $"Hata: '{filePath}' dosyası salt okunur."
                : null;
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
            sb.AppendLine($"İçerik eşleşmeleri: '{query}' ({matches.Count} adet)");
            foreach (var match in matches)
            {
                sb.AppendLine($"  - {match}");
            }

            if (truncated)
            {
                sb.AppendLine("... sonuçlar sınırlandı.");
            }

            return sb.ToString();
        }

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
