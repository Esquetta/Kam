using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Infrastructure.Services.Application
{
    public class LinuxApplicationService : IApplicationService
    {
        private readonly Dictionary<string, string> _applicationCache = new();
        private readonly SemaphoreSlim _cacheSemaphore = new(1, 1);
        private DateTime _lastCacheUpdate = DateTime.MinValue;
        private readonly TimeSpan _cacheValidityPeriod = TimeSpan.FromMinutes(10);

        public async Task OpenApplicationAsync(string appName)
        {
            var executablePath = await FindApplicationExecutableAsync(appName);

            if (!string.IsNullOrEmpty(executablePath))
            {
                // Tam path ile başlat
                ExecuteBashCommand($"nohup '{executablePath}' > /dev/null 2>&1 &");
            }
            else
            {
                // Son çare: orijinal isimle dene
                try
                {
                    ExecuteBashCommand($"nohup {appName} > /dev/null 2>&1 &");
                }
                catch
                {
                    throw new FileNotFoundException($"Application '{appName}' could not be found or started.");
                }
            }
        }

        public Task<AppStatus> GetApplicationStatusAsync(string appName)
        {
            // Önce process ismiyle ara
            var output = ExecuteBashCommand($"pgrep -x {appName}");
            if (!string.IsNullOrWhiteSpace(output))
            {
                return Task.FromResult(AppStatus.Running);
            }

            // Sonra executable ismiyle ara
            output = ExecuteBashCommand($"pgrep -f {appName}");
            var isRunning = !string.IsNullOrWhiteSpace(output);
            return Task.FromResult(isRunning ? AppStatus.Running : AppStatus.Stopped);
        }

        public Task CloseApplicationAsync(string appName)
        {
            // Önce SIGTERM ile nazikçe kapat
            ExecuteBashCommand($"pkill {appName}");

            // 2 saniye bekle
            Thread.Sleep(2000);

            // Hala çalışıyorsa SIGKILL ile zorla kapat
            var stillRunning = ExecuteBashCommand($"pgrep -x {appName}");
            if (!string.IsNullOrWhiteSpace(stillRunning))
            {
                ExecuteBashCommand($"pkill -9 {appName}");
            }

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

        /// <summary>
        /// Linux'ta uygulama executable dosyasını bulur - hibrit arama sistemi
        /// </summary>
        public async Task<string> FindApplicationExecutableAsync(string appName)
        {
            // 1. Önce real-time hızlı arama yap
            var quickResult = await PerformQuickSearchAsync(appName);
            if (!string.IsNullOrEmpty(quickResult))
            {
                return quickResult;
            }

            // 2. Cache kontrolü
            await _cacheSemaphore.WaitAsync();
            try
            {
                if (DateTime.Now - _lastCacheUpdate > _cacheValidityPeriod)
                {
                    await RefreshApplicationCacheAsync();
                }

                // Cache'de ara
                var cacheResult = SearchInCache(appName);
                if (!string.IsNullOrEmpty(cacheResult))
                {
                    return cacheResult;
                }
            }
            finally
            {
                _cacheSemaphore.Release();
            }

            // 3. Cache'de bulunamazsa detaylı real-time arama yap
            return await PerformDetailedSearchAsync(appName);
        }

        /// <summary>
        /// Hızlı real-time arama - en yaygın lokasyonları kontrol eder
        /// </summary>
        private async Task<string> PerformQuickSearchAsync(string appName)
        {
            // Running processes'lerde ara
            var runningResult = SearchInRunningProcesses(appName);
            if (!string.IsNullOrEmpty(runningResult)) return runningResult;

            // PATH environment variable'da ara
            var pathResult = SearchInPathEnvironment(appName);
            if (!string.IsNullOrEmpty(pathResult)) return pathResult;

            // which komutu ile ara
            var whichResult = ExecuteBashCommand($"which {appName}").Trim();
            if (!string.IsNullOrEmpty(whichResult) && File.Exists(whichResult))
            {
                return whichResult;
            }

            // whereis komutu ile ara
            var whereisResult = ExecuteBashCommand($"whereis -b {appName}").Trim();
            if (!string.IsNullOrEmpty(whereisResult))
            {
                var parts = whereisResult.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1 && File.Exists(parts[1]))
                {
                    return parts[1];
                }
            }

            // Desktop entries'de ara
            var desktopResult = await SearchInDesktopEntriesAsync(appName);
            if (!string.IsNullOrEmpty(desktopResult)) return desktopResult;

            // Yaygın executable dizinlerinde ara
            var commonDirsResult = await SearchInCommonExecutableDirectoriesAsync(appName);
            if (!string.IsNullOrEmpty(commonDirsResult)) return commonDirsResult;

            return null;
        }

        /// <summary>
        /// Çalışan processlerde ara
        /// </summary>
        private string SearchInRunningProcesses(string appName)
        {
            var output = ExecuteBashCommand("ps -e -o comm,cmd --no-headers");
            var lines = output.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line));

            foreach (var line in lines)
            {
                try
                {
                    var parts = line.Trim().Split(new char[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        var processName = parts[0];
                        var fullCommand = parts[1];
                        var executablePath = fullCommand.Split(' ')[0];

                        if (processName.ToLower().Contains(appName.ToLower()) ||
                            appName.ToLower().Contains(processName.ToLower()) ||
                            Path.GetFileName(executablePath).ToLower().Contains(appName.ToLower()))
                        {
                            if (File.Exists(executablePath))
                            {
                                return executablePath;
                            }
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }
            return null;
        }

        /// <summary>
        /// Desktop entries'de ara (.desktop dosyaları)
        /// </summary>
        private async Task<string> SearchInDesktopEntriesAsync(string appName)
        {
            var desktopPaths = new[]
            {
                "/usr/share/applications",
                "/usr/local/share/applications",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/applications")
            };

            foreach (var desktopPath in desktopPaths)
            {
                if (Directory.Exists(desktopPath))
                {
                    var result = await SearchDesktopFilesInDirectory(desktopPath, appName);
                    if (!string.IsNullOrEmpty(result)) return result;
                }
            }
            return null;
        }

        private async Task<string> SearchDesktopFilesInDirectory(string directory, string appName)
        {
            try
            {
                var desktopFiles = Directory.GetFiles(directory, "*.desktop");
                foreach (var desktopFile in desktopFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(desktopFile);
                    if (fileName.ToLower().Contains(appName.ToLower()) ||
                        appName.ToLower().Contains(fileName.ToLower()))
                    {
                        var execLine = await GetExecLineFromDesktopFile(desktopFile);
                        if (!string.IsNullOrEmpty(execLine))
                        {
                            // Exec satırından executable path'i çıkar
                            var execPath = ExtractExecutableFromExecLine(execLine);
                            if (!string.IsNullOrEmpty(execPath) && File.Exists(execPath))
                            {
                                return execPath;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Access denied
            }
            return null;
        }

        private async Task<string> GetExecLineFromDesktopFile(string desktopFile)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(desktopFile);
                var execLine = lines.FirstOrDefault(line => line.StartsWith("Exec="));
                return execLine?.Substring(5); // "Exec=" kısmını at
            }
            catch
            {
                return null;
            }
        }

        private string ExtractExecutableFromExecLine(string execLine)
        {
            try
            {
                // %U, %F gibi parametreleri temizle
                var cleanExec = Regex.Replace(execLine, @"%[UuFfDdNnickvm]", "").Trim();

                // Çift tırnak içindeki komutu çıkar
                var match = Regex.Match(cleanExec, @"""([^""]+)""");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }

                // İlk kelimeyi al (genellikle executable path'i)
                var firstWord = cleanExec.Split(' ')[0];

                // Eğer / ile başlıyorsa absolute path
                if (firstWord.StartsWith("/"))
                {
                    return firstWord;
                }

                // Değilse which ile tam path'i bul
                var whichResult = ExecuteBashCommand($"which {firstWord}").Trim();
                if (!string.IsNullOrEmpty(whichResult) && File.Exists(whichResult))
                {
                    return whichResult;
                }
            }
            catch
            {
                // Parse error
            }
            return null;
        }

        /// <summary>
        /// Yaygın executable dizinlerinde ara
        /// </summary>
        private async Task<string> SearchInCommonExecutableDirectoriesAsync(string appName)
        {
            var commonDirs = new[]
            {
                "/usr/bin",
                "/usr/local/bin",
                "/bin",
                "/sbin",
                "/usr/sbin",
                "/opt",
                "/snap/bin",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/bin"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "bin")
            };

            foreach (var dir in commonDirs)
            {
                if (Directory.Exists(dir))
                {
                    try
                    {
                        // /opt için özel arama (subdirectory'lerde ara)
                        if (dir == "/opt")
                        {
                            var result = await SearchInOptDirectory(appName);
                            if (!string.IsNullOrEmpty(result)) return result;
                        }
                        else
                        {
                            var files = Directory.GetFiles(dir);
                            var match = files.FirstOrDefault(f =>
                            {
                                var fileName = Path.GetFileName(f);
                                return fileName.ToLower().Contains(appName.ToLower()) ||
                                       appName.ToLower().Contains(fileName.ToLower());
                            });

                            if (!string.IsNullOrEmpty(match) && IsExecutable(match))
                            {
                                return match;
                            }
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

        private async Task<string> SearchInOptDirectory(string appName)
        {
            try
            {
                var optDirs = Directory.GetDirectories("/opt");
                foreach (var optDir in optDirs)
                {
                    var dirName = Path.GetFileName(optDir);
                    if (dirName.ToLower().Contains(appName.ToLower()) ||
                        appName.ToLower().Contains(dirName.ToLower()))
                    {
                        // bin subdirectory'de ara
                        var binDir = Path.Combine(optDir, "bin");
                        if (Directory.Exists(binDir))
                        {
                            var binFiles = Directory.GetFiles(binDir);
                            var match = binFiles.FirstOrDefault(f => IsExecutable(f));
                            if (!string.IsNullOrEmpty(match))
                            {
                                return match;
                            }
                        }

                        // Doğrudan opt directory'de executable ara
                        var optFiles = Directory.GetFiles(optDir);
                        var directMatch = optFiles.FirstOrDefault(f =>
                        {
                            var fileName = Path.GetFileName(f);
                            return fileName.ToLower().Contains(appName.ToLower()) && IsExecutable(f);
                        });
                        if (!string.IsNullOrEmpty(directMatch))
                        {
                            return directMatch;
                        }
                    }
                }
            }
            catch
            {
                // Access denied
            }
            return null;
        }

        /// <summary>
        /// Detaylı arama - cache'de bulunamazsa kullanılır
        /// </summary>
        private async Task<string> PerformDetailedSearchAsync(string appName)
        {
            // locate komutu ile ara (varsa)
            var locateResult = ExecuteBashCommand($"locate -i '{appName}' | grep -E '/(bin|sbin)/' | head -1").Trim();
            if (!string.IsNullOrEmpty(locateResult) && File.Exists(locateResult) && IsExecutable(locateResult))
            {
                return locateResult;
            }

            // find komutu ile sistem genelinde ara (yavaş olabilir)
            var findResult = ExecuteBashCommand($"find /usr /opt -name '*{appName}*' -type f -executable 2>/dev/null | head -1").Trim();
            if (!string.IsNullOrEmpty(findResult) && File.Exists(findResult))
            {
                return findResult;
            }

            // Snap packages'de ara
            var snapResult = await SearchInSnapPackagesAsync(appName);
            if (!string.IsNullOrEmpty(snapResult)) return snapResult;

            // Flatpak'de ara
            var flatpakResult = await SearchInFlatpakAsync(appName);
            if (!string.IsNullOrEmpty(flatpakResult)) return flatpakResult;

            return null;
        }

        private async Task<string> SearchInSnapPackagesAsync(string appName)
        {
            try
            {
                var snapList = ExecuteBashCommand("snap list --unicode=never 2>/dev/null");
                var lines = snapList.Split('\n').Skip(1); // Header'ı atla

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                    {
                        var snapName = parts[0];
                        if (snapName.ToLower().Contains(appName.ToLower()) ||
                            appName.ToLower().Contains(snapName.ToLower()))
                        {
                            var snapPath = $"/snap/bin/{snapName}";
                            if (File.Exists(snapPath))
                            {
                                return snapPath;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Snap mevcut değil
            }
            return null;
        }

        private async Task<string> SearchInFlatpakAsync(string appName)
        {
            try
            {
                var flatpakList = ExecuteBashCommand("flatpak list --app --columns=application 2>/dev/null");
                var lines = flatpakList.Split('\n');

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (line.ToLower().Contains(appName.ToLower()) ||
                        appName.ToLower().Contains(line.ToLower()))
                    {
                        // Flatpak run komutu ile çalıştırma
                        return $"flatpak run {line.Trim()}";
                    }
                }
            }
            catch
            {
                // Flatpak mevcut değil
            }
            return null;
        }

        /// <summary>
        /// Cache yenileme
        /// </summary>
        private async Task RefreshApplicationCacheAsync()
        {
            _applicationCache.Clear();

            // Running processes'leri cache'le
            await CacheRunningProcessesAsync();

            // Desktop entries'leri cache'le
            await CacheDesktopEntriesAsync();

            // Common directories'leri cache'le
            await CacheCommonDirectoriesAsync();

            _lastCacheUpdate = DateTime.Now;
        }

        private async Task CacheRunningProcessesAsync()
        {
            var output = ExecuteBashCommand("ps -e -o comm,cmd --no-headers");
            var lines = output.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line));

            foreach (var line in lines)
            {
                try
                {
                    var parts = line.Trim().Split(new char[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        var processName = parts[0];
                        var fullCommand = parts[1];
                        var executablePath = fullCommand.Split(' ')[0];

                        if (File.Exists(executablePath))
                        {
                            _applicationCache.TryAdd(processName.ToLower(), executablePath);
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }
        }

        private async Task CacheDesktopEntriesAsync()
        {
            var desktopPaths = new[]
            {
                "/usr/share/applications",
                "/usr/local/share/applications",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/applications")
            };

            foreach (var desktopPath in desktopPaths)
            {
                if (Directory.Exists(desktopPath))
                {
                    await ProcessDesktopDirectory(desktopPath);
                }
            }
        }

        private async Task ProcessDesktopDirectory(string directory)
        {
            try
            {
                var desktopFiles = Directory.GetFiles(directory, "*.desktop");
                foreach (var desktopFile in desktopFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(desktopFile);
                    var execLine = await GetExecLineFromDesktopFile(desktopFile);
                    if (!string.IsNullOrEmpty(execLine))
                    {
                        var execPath = ExtractExecutableFromExecLine(execLine);
                        if (!string.IsNullOrEmpty(execPath) && File.Exists(execPath))
                        {
                            _applicationCache.TryAdd(fileName.ToLower(), execPath);
                        }
                    }
                }
            }
            catch
            {
                // Access denied
            }
        }

        private async Task CacheCommonDirectoriesAsync()
        {
            var commonDirs = new[]
            {
                "/usr/bin",
                "/usr/local/bin",
                "/bin",
                "/snap/bin"
            };

            foreach (var dir in commonDirs)
            {
                if (Directory.Exists(dir))
                {
                    try
                    {
                        var files = Directory.GetFiles(dir);
                        foreach (var file in files)
                        {
                            if (IsExecutable(file))
                            {
                                var fileName = Path.GetFileName(file);
                                _applicationCache.TryAdd(fileName.ToLower(), file);
                            }
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
        }

        private string SearchInCache(string appName)
        {
            var searchKey = appName.ToLower();

            // Exact match
            if (_applicationCache.TryGetValue(searchKey, out string exactMatch))
            {
                return exactMatch;
            }

            // Partial match
            var partialMatch = _applicationCache.FirstOrDefault(kvp =>
                kvp.Key.Contains(searchKey) || searchKey.Contains(kvp.Key));

            return partialMatch.Value;
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
                            return fileName.ToLower().Contains(appName.ToLower()) && IsExecutable(f);
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

        private bool IsExecutable(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                // Linux'ta executable kontrolü için stat komutunu kullan
                var result = ExecuteBashCommand($"test -x '{filePath}' && echo 'executable'");
                return result.Trim() == "executable";
            }
            catch
            {
                return false;
            }
        }

        // Existing methods...
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

                    var executablePath = fullCommand.Split(' ')[0];
                    var processStatus = GetLinuxProcessStatus(status);

                    return new AppInfoDTO(processName, executablePath, processStatus == "Running");
                }
                else if (parts.Length >= 3)
                {
                    var processName = parts[1];
                    var fullCommand = parts[2];

                    var executablePath = fullCommand.Split(' ')[0];

                    return new AppInfoDTO(processName, executablePath, true);
                }
            }
            catch (Exception)
            {
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

        // Sadece çalışan uygulamaları listele
        public Task<IEnumerable<AppInfoDTO>> ListRunningApplicationsAsync()
        {
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

                    return new AppInfoDTO(processName, executablePath, processStatus == "Running");
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