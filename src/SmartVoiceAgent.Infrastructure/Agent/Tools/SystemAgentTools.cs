using AgentFrameworkToolkit.Tools;
using MediatR;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Commands;
using SmartVoiceAgent.Infrastructure.Agent.Tools;
using System.ComponentModel;

namespace SmartVoiceAgent.Infrastructure.Agent.Functions
{
    /// <summary>
    /// System Agent Functions for desktop application management, device control, and file operations.
    /// </summary>
    public sealed class SystemAgentTools
    {
        private readonly IMediator _mediator;
        private readonly ConversationContextManager _contextManager;
        private readonly FileAgentTools _fileTools;

        public SystemAgentTools(
            IMediator mediator,
            ConversationContextManager contextManager,
            FileAgentTools fileTools)
        {
            _mediator = mediator;
            _contextManager = contextManager;
            _fileTools = fileTools;
        }

        #region Application Management

        [AITool("open_application_async", "Opens a desktop application by name.")]
        public async Task<string> OpenApplicationAsync(
            [Description("Name of the application to open (e.g., Chrome, Spotify)")]
            string applicationName)
        {
            Console.WriteLine($"SystemAgent: Opening {applicationName}");

            if (_contextManager.IsApplicationOpen(applicationName))
            {
                return $"{applicationName} uygulaması zaten şu an açık.";
            }

            try
            {
                var result = await _mediator.Send(new OpenApplicationCommand(applicationName));
                _contextManager.SetApplicationState(applicationName, true);
                _contextManager.UpdateContext("app_open", applicationName, "Success");
                return $"{applicationName} başarıyla başlatıldı.";
            }
            catch (Exception ex)
            {
                _contextManager.UpdateContext("app_open_error", applicationName, ex.Message);
                return $"{applicationName} açılamadı. Hata: {ex.Message}";
            }
        }

        [AITool("close_application", "Closes a running desktop application safely.")]
        public async Task<string> CloseApplicationAsync(
            [Description("Name of the application to close")]
            string applicationName)
        {
            Console.WriteLine($"SystemAgent: Closing {applicationName}");

            if (!_contextManager.IsApplicationOpen(applicationName))
            {
                return $"{applicationName} zaten kapalı veya çalışmıyor.";
            }

            try
            {
                await _mediator.Send(new CloseApplicationCommand(applicationName));
                _contextManager.SetApplicationState(applicationName, false);
                _contextManager.UpdateContext("app_close", applicationName, "Success");
                return $"{applicationName} başarıyla kapatıldı.";
            }
            catch (Exception ex)
            {
                return $"{applicationName} kapatılırken bir hata oluştu: {ex.Message}";
            }
        }

        [AITool("check_application_status", "Checks if an application is installed and returns diagnostic info.")]
        public async Task<string> CheckApplicationAsync(
            [Description("Name of the application to verify")]
            string applicationName)
        {
            try
            {
                var result = await _mediator.Send(new CheckApplicationCommand(applicationName));
                return result?.ToString() ?? $"{applicationName} hakkında bilgi bulunamadı.";
            }
            catch (Exception ex)
            {
                return $"Kontrol sırasında hata: {ex.Message}";
            }
        }

        [AITool("get_application_path", "Retrieves the full installation path for an application.")]
        public async Task<string> GetApplicationPathAsync(string applicationName)
        {
            try
            {
                var result = await _mediator.Send(new GetApplicationPathCommand(applicationName));
                return result?.ToString() ?? $"{applicationName} için dosya yolu bulunamadı.";
            }
            catch (Exception ex)
            {
                return $"Dosya yolu alınamadı: {ex.Message}";
            }
        }

        [AITool("is_application_running", "Checks if an application is currently running.")]
        public async Task<string> IsApplicationRunningAsync(string applicationName)
        {
            try
            {
                var result = await _mediator.Send(new IsApplicationRunningCommand(applicationName));
                return result.ToString();
            }
            catch (Exception ex)
            {
                return $"Durum kontrolü başarısız: {ex.Message}";
            }
        }

        [AITool("list_installed_applications", "Lists all installed applications on the system.")]
        public async Task<string> ListInstalledApplicationsAsync(bool includeSystemApps = false)
        {
            try
            {
                var result = await _mediator.Send(new ListInstalledApplicationsCommand(includeSystemApps));
                return result?.ToString() ?? "Yüklü uygulama listesi boş.";
            }
            catch (Exception ex)
            {
                return $"Liste alınamadı: {ex.Message}";
            }
        }

        #endregion

        #region Media Control

        [AITool("play_music", "Plays music using available media players.")]
        public async Task<string> PlayMusicAsync(
            [Description("Name of the track, playlist, etc.")] string trackName)
        {
            try
            {
                var result = await _mediator.Send(new PlayMusicCommand(trackName));
                _contextManager.UpdateContext("music_play", trackName, "Started");
                return result?.ToString() ?? $"{trackName} çalınmaya başlandı.";
            }
            catch (Exception ex)
            {
                return $"{trackName} çalınamadı. Hata: {ex.Message}";
            }
        }

        #endregion

        #region Device Control

        [AITool("control_device", "Controls system devices and hardware components.")]
        public async Task<string> ControlDeviceAsync(
            [Description("Name of the device (volume, wifi, etc)")] string deviceName,
            [Description("Action (increase, toggle, on, off)")] string action)
        {
            try
            {
                var result = await _mediator.Send(new ControlDeviceCommand(deviceName, action));
                return result?.ToString() ?? $"{deviceName} üzerinde {action} işlemi uygulandı.";
            }
            catch (Exception ex)
            {
                return $"{deviceName} cihazı kontrol edilemedi: {ex.Message}";
            }
        }

        #endregion

        #region File Operations (Delegated to FileAgentTools)

        [AITool("read_file", "Reads the content of a file from the file system.")]
        public async Task<string> ReadFileAsync(
            [Description("Full path to the file to read")]
            string filePath)
        {
            return await _fileTools.ReadFileAsync(filePath);
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
            return await _fileTools.WriteFileAsync(filePath, content, append);
        }

        [AITool("create_file", "Creates a new file with optional initial content.")]
        public async Task<string> CreateFileAsync(
            [Description("Full path to the file to create")]
            string filePath,
            [Description("Initial content for the file (optional)")]
            string content = "")
        {
            return await _fileTools.CreateFileAsync(filePath, content);
        }

        [AITool("delete_file", "Deletes a file from the file system.")]
        public async Task<string> DeleteFileAsync(
            [Description("Full path to the file to delete")]
            string filePath)
        {
            return await _fileTools.DeleteFileAsync(filePath);
        }

        [AITool("copy_file", "Copies a file to a new location.")]
        public async Task<string> CopyFileAsync(
            [Description("Source file path")]
            string sourcePath,
            [Description("Destination file path")]
            string destinationPath,
            [Description("If true, overwrites existing file")]
            bool overwrite = false)
        {
            return await _fileTools.CopyFileAsync(sourcePath, destinationPath, overwrite);
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
            return await _fileTools.MoveFileAsync(sourcePath, destinationPath, overwrite);
        }

        [AITool("file_exists", "Checks if a file exists at the specified path.")]
        public async Task<string> FileExistsAsync(
            [Description("Full path to check")]
            string filePath)
        {
            return await _fileTools.FileExistsAsync(filePath);
        }

        [AITool("get_file_info", "Gets detailed information about a file.")]
        public async Task<string> GetFileInfoAsync(
            [Description("Full path to the file")]
            string filePath)
        {
            return await _fileTools.GetFileInfoAsync(filePath);
        }

        [AITool("list_files", "Lists files in a directory with optional filter.")]
        public async Task<string> ListFilesAsync(
            [Description("Directory path to list files from")]
            string directoryPath,
            [Description("Search pattern (e.g., '*.txt', '*.json')")]
            string searchPattern = "*.*",
            [Description("If true, searches subdirectories")]
            bool recursive = false)
        {
            return await _fileTools.ListFilesAsync(directoryPath, searchPattern, recursive);
        }

        [AITool("search_files", "Searches for files by name pattern in a directory.")]
        public async Task<string> SearchFilesAsync(
            [Description("Directory to search in")]
            string directoryPath,
            [Description("File name pattern to search for")]
            string searchPattern,
            [Description("If true, searches subdirectories")]
            bool recursive = true)
        {
            return await _fileTools.SearchFilesAsync(directoryPath, searchPattern, recursive);
        }

        [AITool("create_directory", "Creates a new directory.")]
        public async Task<string> CreateDirectoryAsync(
            [Description("Full path of the directory to create")]
            string directoryPath)
        {
            return await _fileTools.CreateDirectoryAsync(directoryPath);
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
            return await _fileTools.ReadLinesAsync(filePath, startLine, lineCount);
        }

        #endregion

        public IEnumerable<AIFunction> GetTools()
        {
            return
            [
                // Application Management
                AIFunctionFactory.Create(OpenApplicationAsync),
                AIFunctionFactory.Create(CloseApplicationAsync),
                AIFunctionFactory.Create(CheckApplicationAsync),
                AIFunctionFactory.Create(GetApplicationPathAsync),
                AIFunctionFactory.Create(IsApplicationRunningAsync),
                AIFunctionFactory.Create(ListInstalledApplicationsAsync),
                
                // Media Control
                AIFunctionFactory.Create(PlayMusicAsync),
                
                // Device Control
                AIFunctionFactory.Create(ControlDeviceAsync),
                
                // File Operations
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
    }
}