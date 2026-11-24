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

        public async Task<string> OpenApplicationAsync(
            [Description("Name of the application to open (e.g., Chrome, Spotify)")]
            string applicationName)
        {
            Console.WriteLine($"SystemAgent: Opening {applicationName}");

            if (_contextManager.IsApplicationOpen(applicationName))
            {
                return $"{applicationName} is already open.";
            }

            try
            {
                var result = await _mediator.SendAsync(new OpenApplicationCommand(applicationName));
                _contextManager.SetApplicationState(applicationName, true);
                _contextManager.UpdateContext("app_open", applicationName, "Success");
                return $"{applicationName} was started.";
            }
            catch (Exception ex)
            {
                _contextManager.UpdateContext("app_open_error", applicationName, ex.Message);
                return $"Error: could not open {applicationName}: {ex.Message}";
            }
        }

        public async Task<string> CloseApplicationAsync(
            [Description("Name of the application to close")]
            string applicationName)
        {
            Console.WriteLine($"SystemAgent: Closing {applicationName}");

            if (!_contextManager.IsApplicationOpen(applicationName))
            {
                return $"{applicationName} is already closed or not running.";
            }

            try
            {
                await _mediator.SendAsync(new CloseApplicationCommand(applicationName));
                _contextManager.SetApplicationState(applicationName, false);
                _contextManager.UpdateContext("app_close", applicationName, "Success");
                return $"{applicationName} was closed.";
            }
            catch (Exception ex)
            {
                return $"Error closing {applicationName}: {ex.Message}";
            }
        }

        public async Task<string> CheckApplicationAsync(
            [Description("Name of the application to verify")]
            string applicationName)
        {
            try
            {
                var result = await _mediator.SendAsync(new CheckApplicationCommand(applicationName));
                return result?.ToString() ?? $"No information found for {applicationName}.";
            }
            catch (Exception ex)
            {
                return $"Error checking application: {ex.Message}";
            }
        }

        public async Task<string> GetApplicationPathAsync(string applicationName)
        {
            try
            {
                var result = await _mediator.SendAsync(new GetApplicationPathCommand(applicationName));
                return result?.ToString() ?? $"No file path found for {applicationName}.";
            }
            catch (Exception ex)
            {
                return $"Error getting file path: {ex.Message}";
            }
        }

        public async Task<string> IsApplicationRunningAsync(string applicationName)
        {
            try
            {
                var result = await _mediator.SendAsync(new IsApplicationRunningCommand(applicationName));
                return result.ToString();
            }
            catch (Exception ex)
            {
                return $"Error checking status: {ex.Message}";
            }
        }

        public async Task<string> ListInstalledApplicationsAsync(bool includeSystemApps = false)
        {
            try
            {
                var result = await _mediator.SendAsync(new ListInstalledApplicationsCommand(includeSystemApps));
                return result?.ToString() ?? "The installed application list is empty.";
            }
            catch (Exception ex)
            {
                return $"Error listing applications: {ex.Message}";
            }
        }

        #endregion

        #region Media Control

        public async Task<string> PlayMusicAsync(
            [Description("Name of the track, playlist, etc.")] string trackName)
        {
            try
            {
                var result = await _mediator.SendAsync(new PlayMusicCommand(trackName));
                _contextManager.UpdateContext("music_play", trackName, "Started");
                return result?.ToString() ?? $"Started playing {trackName}.";
            }
            catch (Exception ex)
            {
                return $"Error: could not play {trackName}: {ex.Message}";
            }
        }

        #endregion

        #region Device Control

        public async Task<string> ControlDeviceAsync(
            [Description("Name of the device (volume, wifi, etc)")] string deviceName,
            [Description("Action (increase, toggle, on, off)")] string action)
        {
            try
            {
                var result = await _mediator.SendAsync(new ControlDeviceCommand(deviceName, action));
                return result?.ToString() ?? $"Applied {action} to {deviceName}.";
            }
            catch (Exception ex)
            {
                return $"Error controlling {deviceName}: {ex.Message}";
            }
        }

        #endregion

        #region File Operations (Delegated to FileAgentTools)

        public async Task<string> ReadFileAsync(
            [Description("Full path to the file to read")]
            string filePath)
        {
            return await _fileTools.ReadFileAsync(filePath);
        }

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

        public async Task<string> CreateFileAsync(
            [Description("Full path to the file to create")]
            string filePath,
            [Description("Initial content for the file (optional)")]
            string content = "")
        {
            return await _fileTools.CreateFileAsync(filePath, content);
        }

        public async Task<string> DeleteFileAsync(
            [Description("Full path to the file to delete")]
            string filePath)
        {
            return await _fileTools.DeleteFileAsync(filePath);
        }

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

        public async Task<string> FileExistsAsync(
            [Description("Full path to check")]
            string filePath)
        {
            return await _fileTools.FileExistsAsync(filePath);
        }

        public async Task<string> GetFileInfoAsync(
            [Description("Full path to the file")]
            string filePath)
        {
            return await _fileTools.GetFileInfoAsync(filePath);
        }

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

        public async Task<string> CreateDirectoryAsync(
            [Description("Full path of the directory to create")]
            string directoryPath)
        {
            return await _fileTools.CreateDirectoryAsync(directoryPath);
        }

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
    }
}