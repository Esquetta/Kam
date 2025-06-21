using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;
using System.ComponentModel;
using System.Diagnostics;

namespace SmartVoiceAgent.Infrastructure.Services.Application
{
    public class WindowsApplicationService : IApplicationService
    {
        public Task CloseApplicationAsync(string appName)
        {
            var processes = Process.GetProcessesByName(appName);
            foreach (var process in processes)
            {
                process.Kill();
            }
            return Task.CompletedTask;
        }

        public Task<AppStatus> GetApplicationStatusAsync(string appName)
        {
            var isRunning = Process.GetProcessesByName(appName).Any();
            return Task.FromResult(isRunning ? AppStatus.Running : AppStatus.Stopped);
        }

        public Task<IEnumerable<AppInfoDTO>> ListApplicationsAsync()
        {
            var processes = Process.GetProcesses()
                                 .Select(p => new AppInfoDTO(p.ProcessName, GetProcessInfo(p), p.Responding))
                                 .ToList();
            return Task.FromResult<IEnumerable<AppInfoDTO>>(processes);
        }

        public Task OpenApplicationAsync(string appName)
        {
            Process.Start(new ProcessStartInfo(appName) { UseShellExecute = true });
            return Task.CompletedTask;
        }
        private string GetProcessInfo(Process process)
        {
            try
            {
                // Dosya yolu almaya çalış
                var path = process.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path))
                    return path;

                return "System Process";
            }
            catch (Win32Exception)
            {
                // 32-bit process'ten 64-bit process'e erişim sorunu
                return "Access Denied";
            }
            catch (InvalidOperationException)
            {
                // Process çıkmış
                return "Process Exited";
            }
            catch (Exception)
            {
                return "Unknown";
            }
        }
    }
}
