using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;
using System.Diagnostics;

namespace SmartVoiceAgent.Infrastructure.Services.Application
{
    public class LinuxApplicationService : IApplicationService
    {
        public Task OpenApplicationAsync(string appName)
        {
            Process.Start("bash", $"-c \"{appName} &\"");
            return Task.CompletedTask;
        }

        public Task<AppStatus> GetApplicationStatusAsync(string appName)
        {
            var output = ExecuteBashCommand($"pgrep -x {appName}");
            var isRunning = !string.IsNullOrWhiteSpace(output);
            return Task.FromResult(isRunning ? AppStatus.Running : AppStatus.Stopped);
        }

        public Task CloseApplicationAsync(string appName)
        {
            ExecuteBashCommand($"pkill {appName}");
            return Task.CompletedTask;
        }

        public Task<IEnumerable<AppInfoDTO>> ListApplicationsAsync()
        {
            // ps komutunu PID, process adı, tam dosya yolu ve status için genişletiyoruz
            var output = ExecuteBashCommand("ps -e -o pid,comm,cmd,stat --no-headers");
            var apps = output.Split('\n')
                             .Where(line => !string.IsNullOrWhiteSpace(line))
                             .Select(line => ParseProcessLine(line))
                             .Where(app => app != null)
                             .ToList();

            return Task.FromResult<IEnumerable<AppInfoDTO>>(apps);
        }

        private AppInfoDTO ParseProcessLine(string line)
        {
            try
            {
                var parts = line.Trim().Split(new char[] { ' ' }, 4, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 4)
                {
                    var processName = parts[1];
                    var fullCommand = parts[2];
                    var status = parts[3];

                    // Tam dosya yolunu al (ilk kelime genellikle dosya yolu)
                    var executablePath = fullCommand.Split(' ')[0];
                    var processStatus = GetLinuxProcessStatus(status);

                    return new AppInfoDTO(processName, executablePath, processStatus == "Runing");
                }
                else if (parts.Length >= 3)
                {
                    var processName = parts[1];
                    var fullCommand = parts[2];

                    var executablePath = fullCommand.Split(' ')[0];

                    return new AppInfoDTO(processName, executablePath,true);
                }
            }
            catch (Exception)
            {
                // Parse hatası durumunda null döndür
                return null;
            }

            return null;
        }

        private string GetLinuxProcessStatus(string status)
        {
            var statusChar = status.FirstOrDefault();
            return statusChar switch
            {
                'R' => "Running",
                'S' => "Sleeping",
                'D' => "Waiting",
                'Z' => "Zombie",
                'T' => "Stopped",
                'I' => "Idle",
                _ => "Unknown"
            };
        }

        private string ExecuteBashCommand(string command)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = $"-c \"{command}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string result = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                return result;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        // Alternatif: Sadece çalışan uygulamaları listele
        public Task<IEnumerable<AppInfoDTO>> ListRunningApplicationsAsync()
        {
            // Sadece çalışan (R) ve uyuyan (S) process'leri listele
            var output = ExecuteBashCommand("ps -e -o comm,cmd,stat --no-headers | grep -E '^[^[:space:]]+[[:space:]]+[^[:space:]]+[[:space:]]+[RS]'");
            var apps = output.Split('\n')
                             .Where(line => !string.IsNullOrWhiteSpace(line))
                             .Select(line => ParseRunningProcessLine(line))
                             .Where(app => app != null)
                             .ToList();

            return Task.FromResult<IEnumerable<AppInfoDTO>>(apps);
        }

        private AppInfoDTO ParseRunningProcessLine(string line)
        {
            try
            {
                var parts = line.Trim().Split(new char[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 3)
                {
                    var processName = parts[0];
                    var fullCommand = parts[1];
                    var status = parts[2];

                    var executablePath = fullCommand.Split(' ')[0];
                    var processStatus = GetLinuxProcessStatus(status);

                    return new AppInfoDTO(processName, executablePath, processStatus=="Runing");
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
        }
    }
}