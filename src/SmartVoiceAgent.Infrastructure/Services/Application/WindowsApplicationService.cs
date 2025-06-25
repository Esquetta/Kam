using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;
using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using Microsoft.Win32;

namespace SmartVoiceAgent.Infrastructure.Services.Application
{
    public class WindowsApplicationService : IApplicationService
    {
        private readonly Dictionary<string, string> _commonApplications = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"notepad", "notepad.exe"},
            {"calculator", "calc.exe"},
            {"paint", "mspaint.exe"},
            {"chrome", "chrome.exe"},
            {"firefox", "firefox.exe"},
            {"explorer", "explorer.exe"},
            {"cmd", "cmd.exe"},
            {"powershell", "powershell.exe"},
            {"word", "winword.exe"},
            {"excel", "excel.exe"},
            {"powerpoint", "powerpnt.exe"},
            {"outlook", "outlook.exe"},
            {"teams", "Teams.exe"},
            {"skype", "Skype.exe"},
            {"discord", "Discord.exe"},
            {"spotify", "Spotify.exe"},
            {"vlc", "vlc.exe"},
            {"photoshop", "Photoshop.exe"},
            {"vscode", "Code.exe"},
            {"visual studio", "devenv.exe"}
        };

        public Task CloseApplicationAsync(string appName)
        {
            var processes = Process.GetProcessesByName(appName);
            foreach (var process in processes)
            {
                process.Kill();
            }
            return Task.CompletedTask;
        }

        public async Task<AppStatus> GetApplicationStatusAsync(string appName)
        {
            var processes = Process.GetProcesses();
            foreach (var process in processes)
            {
                try
                {
                    var fileName = process.MainModule?.FileName;
                    if (fileName != null && fileName.ToLower().Contains(appName.ToLower()))
                    {
                        return AppStatus.Running;
                    }
                }
                catch
                {
                    // Bazı sistem process'lerinde erişim izni yok — geç
                    continue;
                }
            }
            // Eğer buraya geldiyse WMI ile kontrol et
            var searcher = new ManagementObjectSearcher("SELECT ExecutablePath FROM Win32_Process");
            foreach (ManagementObject obj in searcher.Get())
            {
                var path = obj["ExecutablePath"]?.ToString();
                if (path != null && path.ToLower().Contains(appName.ToLower()))
                {
                    return AppStatus.Running;
                }
            }
            return AppStatus.Stopped;
        }

        public Task<IEnumerable<AppInfoDTO>> ListApplicationsAsync()
        {
            var processes = Process.GetProcesses();
            var appList = new List<AppInfoDTO>();
            foreach (var process in processes)
            {
                try
                {
                    var path = process.MainModule?.FileName ?? "System Process";
                    var name = process.ProcessName;
                    var isResponding = process.Responding;
                    appList.Add(new AppInfoDTO(name, path, isResponding));
                }
                catch (Win32Exception)
                {
                    // 32bit-64bit erişim hatası, es geç
                    continue;
                }
                catch (InvalidOperationException)
                {
                    // Process bitmiş olabilir
                    continue;
                }
                catch
                {
                    // Diğer bilinmeyen hatalar
                    continue;
                }
            }
            return Task.FromResult<IEnumerable<AppInfoDTO>>(appList);
        }

        public async Task OpenApplicationAsync(string appName)
        {
            var executablePath = await FindApplicationExecutableAsync(appName);

            if (!string.IsNullOrEmpty(executablePath))
            {
                Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });
            }
            else
            {
                // Eğer exe bulunamazsa, orijinal ismi ile denemeyi dene
                Process.Start(new ProcessStartInfo(appName) { UseShellExecute = true });
            }
        }

        /// <summary>
        /// Verilen uygulama adından executable dosyasını bulur
        /// </summary>
        /// <param name="appName">Uygulama adı</param>
        /// <returns>Executable dosyasının tam yolu veya null</returns>
        public async Task<string> FindApplicationExecutableAsync(string appName)
        {
            // 1. Önce common applications dictionary'sinden kontrol et
            if (_commonApplications.TryGetValue(appName, out string commonExe))
            {
                // System32 ve diğer system klasörlerinde ara
                var systemPaths = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Internet Explorer"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows NT", "Accessories")
                };

                foreach (var systemPath in systemPaths)
                {
                    var fullPath = Path.Combine(systemPath, commonExe);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
            }

            // 2. Registry'den installed applications kontrol et
            var registryPath = await FindInRegistryAsync(appName);
            if (!string.IsNullOrEmpty(registryPath))
            {
                return registryPath;
            }

            // 3. Program Files klasörlerinde ara
            var programPath = await SearchInProgramFilesAsync(appName);
            if (!string.IsNullOrEmpty(programPath))
            {
                return programPath;
            }

            // 4. PATH environment variable'ında ara
            var pathVariable = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathVariable))
            {
                var paths = pathVariable.Split(';');
                foreach (var path in paths)
                {
                    if (Directory.Exists(path))
                    {
                        var files = Directory.GetFiles(path, "*.exe");
                        var match = files.FirstOrDefault(f =>
                            Path.GetFileNameWithoutExtension(f).ToLower().Contains(appName.ToLower()));
                        if (!string.IsNullOrEmpty(match))
                        {
                            return match;
                        }
                    }
                }
            }

            // 5. Şu anda çalışan process'lerden ara
            var runningProcessPath = await FindInRunningProcessesAsync(appName);
            if (!string.IsNullOrEmpty(runningProcessPath))
            {
                return runningProcessPath;
            }

            return null;
        }

        private async Task<string> FindInRegistryAsync(string appName)
        {
            try
            {
                // Uninstall registry entries'lerinde ara
                var uninstallKeys = new[]
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                };

                foreach (var keyPath in uninstallKeys)
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(keyPath))
                    {
                        if (key != null)
                        {
                            foreach (var subKeyName in key.GetSubKeyNames())
                            {
                                using (var subKey = key.OpenSubKey(subKeyName))
                                {
                                    if (subKey != null)
                                    {
                                        var displayName = subKey.GetValue("DisplayName")?.ToString();
                                        var installLocation = subKey.GetValue("InstallLocation")?.ToString();
                                        var displayIcon = subKey.GetValue("DisplayIcon")?.ToString();

                                        if (!string.IsNullOrEmpty(displayName) &&
                                            displayName.ToLower().Contains(appName.ToLower()))
                                        {
                                            // Install location'dan exe bulmaya çalış
                                            if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                                            {
                                                var exeFiles = Directory.GetFiles(installLocation, "*.exe", SearchOption.AllDirectories);
                                                var mainExe = exeFiles.FirstOrDefault(f =>
                                                    Path.GetFileNameWithoutExtension(f).ToLower().Contains(appName.ToLower()));
                                                if (!string.IsNullOrEmpty(mainExe))
                                                {
                                                    return mainExe;
                                                }
                                            }

                                            // Display icon'dan exe path al
                                            if (!string.IsNullOrEmpty(displayIcon) && displayIcon.EndsWith(".exe"))
                                            {
                                                var iconPath = displayIcon.Split(',')[0]; // Icon index'i varsa ayır
                                                if (File.Exists(iconPath))
                                                {
                                                    return iconPath;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Registry erişim hatası, log yapabilirsin
                Debug.WriteLine($"Registry error: {ex.Message}");
            }

            return null;
        }

        private async Task<string> SearchInProgramFilesAsync(string appName)
        {
            var programDirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
            };

            foreach (var programDir in programDirs)
            {
                if (Directory.Exists(programDir))
                {
                    try
                    {
                        // İlk seviye klasörlerde ara
                        var subdirs = Directory.GetDirectories(programDir);
                        foreach (var subdir in subdirs)
                        {
                            var dirName = Path.GetFileName(subdir);
                            if (dirName.ToLower().Contains(appName.ToLower()))
                            {
                                var exeFiles = Directory.GetFiles(subdir, "*.exe", SearchOption.AllDirectories);
                                var mainExe = exeFiles.FirstOrDefault(f =>
                                    Path.GetFileNameWithoutExtension(f).ToLower().Contains(appName.ToLower()));
                                if (!string.IsNullOrEmpty(mainExe))
                                {
                                    return mainExe;
                                }
                                // Ana exe bulunamazsa, ilk exe dosyasını al
                                if (exeFiles.Length > 0)
                                {
                                    return exeFiles[0];
                                }
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Erişim izni yok, geç
                        continue;
                    }
                }
            }

            return null;
        }

        private async Task<string> FindInRunningProcessesAsync(string appName)
        {
            var processes = Process.GetProcesses();
            foreach (var process in processes)
            {
                try
                {
                    if (process.ProcessName.ToLower().Contains(appName.ToLower()) ||
                        (process.MainModule?.ModuleName?.ToLower().Contains(appName.ToLower()) == true))
                    {
                        return process.MainModule?.FileName;
                    }
                }
                catch
                {
                    continue;
                }
            }
            return null;
        }
    }
}