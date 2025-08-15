using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Infrastructure.Services.Application
{
    public class MacOSApplicationService : IApplicationService
    {
        public Task OpenApplicationAsync(string appName)
        {
            // macOS'ta uygulama açmak için 'open' komutu kullanılır
            if (appName.EndsWith(".app"))
            {
                // .app bundle için
                ExecuteShellCommand($"open -a \"{appName}\"");
            }
            else
            {
                // Diğer executable'lar için
                ExecuteShellCommand($"open \"{appName}\" &");
            }
            return Task.CompletedTask;
        }

        public Task<AppStatus> GetApplicationStatusAsync(string appName)
        {
            // .app uzantısını kaldır
            var processName = appName.Replace(".app", "");
            var output = ExecuteShellCommand($"pgrep -i \"{processName}\"");
            var isRunning = !string.IsNullOrWhiteSpace(output);
            return Task.FromResult(isRunning ? AppStatus.Running : AppStatus.Stopped);
        }

        public Task CloseApplicationAsync(string appName)
        {
            // .app uzantısını kaldır
            var processName = appName.Replace(".app", "");

            // Önce nazikçe kapatmaya çalış
            ExecuteShellCommand($"pkill -i \"{processName}\"");

            // Eğer hala çalışıyorsa force kill
            Task.Delay(2000).ContinueWith(_ =>
            {
                var stillRunning = ExecuteShellCommand($"pgrep -i \"{processName}\"");
                if (!string.IsNullOrWhiteSpace(stillRunning))
                {
                    ExecuteShellCommand($"pkill -9 -i \"{processName}\"");
                }
            });

            return Task.CompletedTask;
        }

        public Task<IEnumerable<AppInfoDTO>> ListApplicationsAsync()
        {
            // macOS'ta ps komutu biraz farklı parametreler alır
            var output = ExecuteShellCommand("ps -e -o comm,args,state");
            var apps = output.Split('\n')
                             .Skip(1) // Header satırını atla
                             .Where(line => !string.IsNullOrWhiteSpace(line))
                             .Select(line => ParseProcessLine(line))
                             .Where(app => app != null)
                             .DistinctBy(app => app.Name) // Aynı isimde birden fazla process varsa birini al
                             .ToList();

            return Task.FromResult<IEnumerable<AppInfoDTO>>(apps);
        }

        public Task<IEnumerable<AppInfoDTO>> ListMacOSApplicationsAsync()
        {
            // macOS'a özel .app bundle'ları da listele
            var processApps = ListApplicationsAsync().Result;
            var installedApps = GetInstalledApplications();

            // İkisini birleştir
            var allApps = processApps.Concat(installedApps)
                                   .DistinctBy(app => app.Name)
                                   .ToList();

            return Task.FromResult<IEnumerable<AppInfoDTO>>(allApps);
        }

        private IEnumerable<AppInfoDTO> GetInstalledApplications()
        {
            try
            {
                // /Applications klasöründeki .app bundle'ları listele
                var appsOutput = ExecuteShellCommand("find /Applications -name '*.app' -maxdepth 2 2>/dev/null");
                var systemAppsOutput = ExecuteShellCommand("find /System/Applications -name '*.app' -maxdepth 2 2>/dev/null");

                var allAppPaths = (appsOutput + "\n" + systemAppsOutput).Split('\n')
                                                                        .Where(path => !string.IsNullOrWhiteSpace(path));

                var apps = new List<AppInfoDTO>();
                foreach (var appPath in allAppPaths)
                {
                    var appName = Path.GetFileNameWithoutExtension(appPath);
                    var isRunning = IsApplicationRunning(appName);
                    var status = isRunning ? "Running" : "Stopped";

                    apps.Add(new AppInfoDTO(appName, appPath, isRunning));
                }

                return apps;
            }
            catch
            {
                return new List<AppInfoDTO>();
            }
        }

        private bool IsApplicationRunning(string appName)
        {
            var output = ExecuteShellCommand($"pgrep -i \"{appName}\"");
            return !string.IsNullOrWhiteSpace(output);
        }

        private AppInfoDTO ParseProcessLine(string line)
        {
            try
            {
                // macOS ps çıktısı: COMMAND ARGS STATE
                var trimmedLine = line.Trim();
                var parts = trimmedLine.Split(new char[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 3)
                {
                    var processName = parts[0];
                    var fullCommand = parts[1];
                    var state = parts[2];

                    // Process adını temizle (path'ten sadece dosya adını al)
                    var cleanName = Path.GetFileName(processName);
                    if (string.IsNullOrEmpty(cleanName))
                        cleanName = processName;

                    // Tam dosya yolunu al
                    var executablePath = fullCommand.Split(' ')[0];
                    var processStatus = GetMacOSProcessStatus(state);

                    return new AppInfoDTO(cleanName, executablePath, processStatus == "Runing" ? true : false);
                }
                else if (parts.Length >= 2)
                {
                    var processName = parts[0];
                    var fullCommand = parts[1];

                    var cleanName = Path.GetFileName(processName);
                    if (string.IsNullOrEmpty(cleanName))
                        cleanName = processName;

                    var executablePath = fullCommand.Split(' ')[0];

                    return new AppInfoDTO(cleanName, executablePath, true);
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
        }

        private string GetMacOSProcessStatus(string state)
        {
            // macOS process state'leri
            return state.ToUpper() switch
            {
                "R" => "Running",      // Runnable
                "S" => "Sleeping",     // Sleeping
                "I" => "Idle",         // Idle
                "T" => "Stopped",      // Stopped
                "U" => "Uninterruptible", // Uninterruptible wait
                "Z" => "Zombie",       // Zombie
                _ => "Unknown"
            };
        }

        private string ExecuteShellCommand(string command)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/zsh", // macOS varsayılan shell zsh
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

        // macOS'a özel: Dock'taki uygulamaları listele
        public Task<IEnumerable<AppInfoDTO>> ListDockApplicationsAsync()
        {
            try
            {
                // AppleScript ile Dock'taki uygulamaları al
                var appleScript = @"
                    tell application ""System Events""
                        set dockApps to name of every application process whose background only is false
                        return dockApps as string
                    end tell
                ";

                var output = ExecuteShellCommand($"osascript -e '{appleScript}'");
                var dockApps = output.Split(',')
                                   .Select(app => app.Trim())
                                   .Where(app => !string.IsNullOrEmpty(app))
                                   .Select(app => new AppInfoDTO(app, GetApplicationPath(app), true))
                                   .ToList();

                return Task.FromResult<IEnumerable<AppInfoDTO>>(dockApps);
            }
            catch
            {
                return Task.FromResult<IEnumerable<AppInfoDTO>>(new List<AppInfoDTO>());
            }
        }

        private string GetApplicationPath(string appName)
        {
            try
            {
                // Uygulamanın tam yolunu bul
                var output = ExecuteShellCommand($"mdfind \"kMDItemKind == 'Application' && kMDItemDisplayName == '{appName}'\"");
                var firstPath = output.Split('\n').FirstOrDefault(path => !string.IsNullOrEmpty(path));
                return firstPath ?? $"/Applications/{appName}.app";
            }
            catch
            {
                return $"/Applications/{appName}.app";
            }
        }
        public async Task<ApplicationInstallInfo> CheckApplicationInstallationAsync(string appName)
        {
            try
            {
                var executablePath = await FindApplicationExecutableAsync(appName);

                if (!string.IsNullOrEmpty(executablePath))
                {
                    var displayName = GetApplicationDisplayName(executablePath, appName);
                    var version = GetApplicationVersion(executablePath);
                    var installDate = GetApplicationInstallDate(executablePath);

                    return new ApplicationInstallInfo(
                        true,
                        executablePath,
                        displayName,
                        version,
                        installDate
                    );
                }

                return new ApplicationInstallInfo(false, null, appName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking application installation: {ex.Message}");
                return new ApplicationInstallInfo(false, null, appName);
            }
        }

        public async Task<string> GetApplicationExecutablePathAsync(string appName)
        {
            var installInfo = await CheckApplicationInstallationAsync(appName);
            return installInfo.IsInstalled ? installInfo.ExecutablePath : null;
        }

        private async Task<string> FindApplicationExecutableAsync(string appName)
        {
            // .app bundle'ları ara
            var appPaths = new[]
            {
                "/Applications",
                "/System/Applications",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications")
            };

            foreach (var appPath in appPaths)
            {
                if (Directory.Exists(appPath))
                {
                    var appBundles = Directory.GetDirectories(appPath, "*.app");
                    var matchingApp = appBundles.FirstOrDefault(app =>
                    {
                        var bundleName = Path.GetFileNameWithoutExtension(app);
                        return bundleName.ToLower().Contains(appName.ToLower()) ||
                               appName.ToLower().Contains(bundleName.ToLower());
                    });

                    if (!string.IsNullOrEmpty(matchingApp))
                        return matchingApp;
                }
            }

            // PATH'de ara
            var pathResult = SearchInPathEnvironment(appName);
            if (!string.IsNullOrEmpty(pathResult))
                return pathResult;

            // mdfind ile ara
            var mdfindResult = ExecuteShellCommand($"mdfind \"kMDItemKind == 'Application' && kMDItemDisplayName == '*{appName}*'\" | head -1");
            if (!string.IsNullOrEmpty(mdfindResult))
                return mdfindResult.Trim();

            return null;
        }

        private string GetApplicationDisplayName(string executablePath, string fallbackName)
        {
            try
            {
                if (executablePath.EndsWith(".app"))
                {
                    // .app bundle için Info.plist'ten al
                    var infoPlistPath = Path.Combine(executablePath, "Contents", "Info.plist");
                    if (File.Exists(infoPlistPath))
                    {
                        var plistContent = File.ReadAllText(infoPlistPath);
                        var displayName = ExtractPlistValue(plistContent, "CFBundleDisplayName") ??
                                         ExtractPlistValue(plistContent, "CFBundleName");

                        if (!string.IsNullOrEmpty(displayName))
                            return displayName;
                    }

                    return Path.GetFileNameWithoutExtension(executablePath);
                }
                else
                {
                    return Path.GetFileNameWithoutExtension(executablePath);
                }
            }
            catch
            {
                return fallbackName;
            }
        }

        private string GetApplicationVersion(string executablePath)
        {
            try
            {
                if (executablePath.EndsWith(".app"))
                {
                    // .app bundle için Info.plist'ten al
                    var infoPlistPath = Path.Combine(executablePath, "Contents", "Info.plist");
                    if (File.Exists(infoPlistPath))
                    {
                        var plistContent = File.ReadAllText(infoPlistPath);
                        return ExtractPlistValue(plistContent, "CFBundleShortVersionString") ??
                               ExtractPlistValue(plistContent, "CFBundleVersion");
                    }
                }
                else
                {
                    // Regular executable için --version dene
                    var versionOutput = ExecuteShellCommand($"'{executablePath}' --version 2>/dev/null | head -1");
                    if (!string.IsNullOrEmpty(versionOutput))
                    {
                        var versionMatch = Regex.Match(versionOutput, @"(\d+\.)*\d+");
                        if (versionMatch.Success)
                            return versionMatch.Value;
                    }
                }
            }
            catch { }
            return null;
        }

        private DateTime? GetApplicationInstallDate(string executablePath)
        {
            try
            {
                if (Directory.Exists(executablePath))
                {
                    var dirInfo = new DirectoryInfo(executablePath);
                    return dirInfo.CreationTime;
                }
                else if (File.Exists(executablePath))
                {
                    var fileInfo = new FileInfo(executablePath);
                    return fileInfo.CreationTime;
                }
            }
            catch { }
            return null;
        }

        private string ExtractPlistValue(string plistContent, string key)
        {
            var keyTag = $"<key>{key}</key>";
            var keyIndex = plistContent.IndexOf(keyTag);
            if (keyIndex == -1) return null;

            var valueStart = plistContent.IndexOf("<string>", keyIndex);
            if (valueStart == -1) return null;

            valueStart += 8; // Length of "<string>"
            var valueEnd = plistContent.IndexOf("</string>", valueStart);
            if (valueEnd == -1) return null;

            return plistContent.Substring(valueStart, valueEnd - valueStart);
        }

        private string SearchInPathEnvironment(string appName)
        {
            var pathVariable = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVariable)) return null;

            var paths = pathVariable.Split(':');
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        var files = Directory.GetFiles(path);
                        var match = files.FirstOrDefault(f =>
                        {
                            var fileName = Path.GetFileName(f);
                            return fileName.ToLower().Contains(appName.ToLower());
                        });
                        if (!string.IsNullOrEmpty(match))
                        {
                            return match;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
            return null;
        }
    }

}