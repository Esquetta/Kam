using AgentFrameworkToolkit.Tools;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text;

namespace SmartVoiceAgent.Infrastructure.Agent.Tools
{
    /// <summary>
    /// File Agent Tools for file system operations including read, write, create, delete, and search.
    /// </summary>
    public sealed class FileAgentTools
    {
        private readonly string _defaultWorkingDirectory;
        private readonly HashSet<string> _allowedExtensions;
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
                ".java", ".cpp", ".h", ".yml", ".yaml", ".ini"
            };
        }

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

                // Check file size
                if (fileInfo.Length > _maxFileSizeBytes)
                {
                    return $"Hata: Dosya çok büyük ({fileInfo.Length / 1024 / 1024}MB). Maksimum boyut: {_maxFileSizeBytes / 1024 / 1024}MB";
                }

                // Check file extension
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
            bool append = false)
        {
            try
            {
                Console.WriteLine($"FileAgent: Writing to file {filePath} (append: {append})");

                var fileInfo = new FileInfo(filePath);

                // Check file extension
                if (!_allowedExtensions.Contains(fileInfo.Extension))
                {
                    return $"Hata: '{fileInfo.Extension}' uzantılı dosyalara yazma desteklenmiyor.";
                }

                // Create directory if it doesn't exist
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

                return $"Dosya başarıyla yazıldı: {filePath}";
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

        [AITool("create_file", "Creates a new file with optional initial content.")]
        public async Task<string> CreateFileAsync(
            [Description("Full path to the file to create")]
            string filePath,
            [Description("Initial content for the file (optional)")]
            string content = "")
        {
            try
            {
                Console.WriteLine($"FileAgent: Creating file {filePath}");

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
                return $"Dosya başarıyla oluşturuldu: {filePath}";
            }
            catch (Exception ex)
            {
                return $"Dosya oluşturma hatası: {ex.Message}";
            }
        }

        [AITool("delete_file", "Deletes a file from the file system.")]
        public Task<string> DeleteFileAsync(
            [Description("Full path to the file to delete")]
            string filePath)
        {
            try
            {
                Console.WriteLine($"FileAgent: Deleting file {filePath}");

                if (!File.Exists(filePath))
                {
                    return Task.FromResult($"Hata: '{filePath}' dosyası bulunamadı.");
                }

                File.Delete(filePath);
                return Task.FromResult($"Dosya başarıyla silindi: {filePath}");
            }
            catch (UnauthorizedAccessException)
            {
                return Task.FromResult($"Hata: '{filePath}' dosyasını silme izni yok.");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Dosya silme hatası: {ex.Message}");
            }
        }

        [AITool("copy_file", "Copies a file to a new location.")]
        public Task<string> CopyFileAsync(
            [Description("Source file path")]
            string sourcePath,
            [Description("Destination file path")]
            string destinationPath,
            [Description("If true, overwrites existing file")]
            bool overwrite = false)
        {
            try
            {
                Console.WriteLine($"FileAgent: Copying {sourcePath} to {destinationPath}");

                if (!File.Exists(sourcePath))
                {
                    return Task.FromResult($"Hata: Kaynak dosya '{sourcePath}' bulunamadı.");
                }

                if (File.Exists(destinationPath) && !overwrite)
                {
                    return Task.FromResult($"Hata: Hedef dosya '{destinationPath}' zaten mevcut. overwrite=true kullanın.");
                }

                var destInfo = new FileInfo(destinationPath);
                if (destInfo.Directory != null && !destInfo.Directory.Exists)
                {
                    destInfo.Directory.Create();
                }

                File.Copy(sourcePath, destinationPath, overwrite);
                return Task.FromResult($"Dosya başarıyla kopyalandı: {sourcePath} -> {destinationPath}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Dosya kopyalama hatası: {ex.Message}");
            }
        }

        [AITool("move_file", "Moves a file to a new location.")]
        public Task<string> MoveFileAsync(
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
                    return Task.FromResult($"Hata: Kaynak dosya '{sourcePath}' bulunamadı.");
                }

                if (File.Exists(destinationPath) && !overwrite)
                {
                    return Task.FromResult($"Hata: Hedef dosya '{destinationPath}' zaten mevcut. overwrite=true kullanın.");
                }

                var destInfo = new FileInfo(destinationPath);
                if (destInfo.Directory != null && !destInfo.Directory.Exists)
                {
                    destInfo.Directory.Create();
                }

                File.Move(sourcePath, destinationPath, overwrite);
                return Task.FromResult($"Dosya başarıyla taşındı: {sourcePath} -> {destinationPath}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Dosya taşıma hatası: {ex.Message}");
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

        [AITool("list_files", "Lists files in a directory with optional filter.")]
        public Task<string> ListFilesAsync(
            [Description("Directory path to list files from")]
            string directoryPath,
            [Description("Search pattern (e.g., '*.txt', '*.json')")]
            string searchPattern = "*.*",
            [Description("If true, searches subdirectories")]
            bool recursive = false)
        {
            try
            {
                if (!Directory.Exists(directoryPath))
                {
                    return Task.FromResult($"Hata: '{directoryPath}' dizini bulunamadı.");
                }

                var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var files = Directory.GetFiles(directoryPath, searchPattern, searchOption);

                if (files.Length == 0)
                {
                    return Task.FromResult($"'{directoryPath}' dizininde dosya bulunamadı.");
                }

                var sb = new StringBuilder();
                sb.AppendLine($"Dizindeki dosyalar ({files.Length} adet):");

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    sb.AppendLine($"  - {fileInfo.Name} ({FormatFileSize(fileInfo.Length)})");
                }

                return Task.FromResult(sb.ToString());
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Dosya listeleme hatası: {ex.Message}");
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

                foreach (var file in files.Take(20)) // Limit to 20 results
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
        public Task<string> CreateDirectoryAsync(
            [Description("Full path of the directory to create")]
            string directoryPath)
        {
            try
            {
                Console.WriteLine($"FileAgent: Creating directory {directoryPath}");

                if (Directory.Exists(directoryPath))
                {
                    return Task.FromResult($"Dizin zaten mevcut: {directoryPath}");
                }

                Directory.CreateDirectory(directoryPath);
                return Task.FromResult($"Dizin başarıyla oluşturuldu: {directoryPath}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Dizin oluşturma hatası: {ex.Message}");
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

        public IEnumerable<AIFunction> GetTools()
        {
            return
            [
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
                AIFunctionFactory.Create(ReadLinesAsync)
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