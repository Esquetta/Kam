using AgentFrameworkToolkit.Tools;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Infrastructure.Security;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace SmartVoiceAgent.Infrastructure.Agent.Tools
{
    /// <summary>
    /// File Agent Tools for file system operations including read, write, create, delete, search, and opening.
    /// </summary>
    public sealed class FileAgentTools
    {
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
    }
}