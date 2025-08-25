using Microsoft.Win32;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;
using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using static System.Net.Mime.MediaTypeNames;

namespace SmartVoiceAgent.Infrastructure.Services.Application
{
    public class WindowsApplicationService : IApplicationService
    {
        private readonly Dictionary<string, string> _applicationCache = new();
        private readonly SemaphoreSlim _cacheSemaphore = new(1, 1);
        private DateTime _lastCacheUpdate = DateTime.MinValue;
        private readonly TimeSpan _cacheValidityPeriod = TimeSpan.FromMinutes(10); // Cache süresini kısalttık

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
                    continue;
                }
            }

            // WMI ile kontrol et
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
                    continue;
                }
                catch (InvalidOperationException)
                {
                    continue;
                }
                catch
                {
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
                // Son çare: orijinal isimle dene
                try
                {
                    Process.Start(new ProcessStartInfo(appName) { UseShellExecute = true });
                }
                catch
                {
                    throw new FileNotFoundException($"Application '{appName}' could not be found or started.");
                }
            }
        }

        /// <summary>
        /// Verilen uygulama adından executable dosyasını bulur - hibrit arama sistemi
        /// </summary>
        public async Task<string> FindApplicationExecutableAsync(string appName)
        {
            // 1. Önce real-time hızlı arama yap (cache olmadan)
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
            // Running processes'lerde ara (en hızlı)
            var runningResult = SearchInRunningProcesses(appName);
            if (!string.IsNullOrEmpty(runningResult)) return runningResult;

            // PATH environment variable'da ara
            var pathResult = SearchInPathEnvironment(appName);
            if (!string.IsNullOrEmpty(pathResult)) return pathResult;

            // Desktop shortcuts'larda ara
            var desktopResult = await SearchInDesktopAsync(appName);
            if (!string.IsNullOrEmpty(desktopResult)) return desktopResult;

            // Start Menu'de ara (hızlı)
            var startMenuResult = await SearchInStartMenuAsync(appName);
            if (!string.IsNullOrEmpty(startMenuResult)) return startMenuResult;

            // Registry App Paths'de ara (VSCode burada olur)
            var appPathsResult = await SearchInRegistryAppPathsAsync(appName);
            if (!string.IsNullOrEmpty(appPathsResult)) return appPathsResult;

            // Yaygın program dizinlerinde yüzeysel arama
            var programDirsResult = await SearchInCommonProgramDirectoriesAsync(appName);
            if (!string.IsNullOrEmpty(programDirsResult)) return programDirsResult;

            return null;
        }

        /// <summary>
        /// Çalışan processlerde ara
        /// </summary>
        private string SearchInRunningProcesses(string appName)
        {
            var processes = Process.GetProcesses();
            foreach (var process in processes)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(path);
                        if (fileName.ToLower().Contains(appName.ToLower()) ||
                            appName.ToLower().Contains(fileName.ToLower()))
                        {
                            return path;
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
        /// Registry App Paths'de ara - VSCode burada kayıtlı
        /// </summary>
        private async Task<string> SearchInRegistryAppPathsAsync(string appName)
        {
            var registryPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths"
            };

            foreach (var registryPath in registryPaths)
            {
                var result = await SearchInRegistryPath(Registry.LocalMachine, registryPath, appName);
                if (!string.IsNullOrEmpty(result)) return result;

                result = await SearchInRegistryPath(Registry.CurrentUser, registryPath, appName);
                if (!string.IsNullOrEmpty(result)) return result;
            }

            return null;
        }

        private async Task<string> SearchInRegistryPath(RegistryKey baseKey, string subKeyPath, string appName)
        {
            try
            {
                using var key = baseKey.OpenSubKey(subKeyPath);
                if (key == null) return null;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    if (subKeyName.ToLower().Contains(appName.ToLower()) ||
                        appName.ToLower().Contains(subKeyName.ToLower().Replace(".exe", "")))
                    {
                        using var subKey = key.OpenSubKey(subKeyName);
                        var exePath = subKey?.GetValue("")?.ToString();
                        if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                        {
                            return exePath;
                        }
                    }
                }
            }
            catch
            {
                // Registry access denied
            }
            return null;
        }

        /// <summary>
        /// Start Menu'de hızlı arama
        /// </summary>
        private async Task<string> SearchInStartMenuAsync(string appName)
        {
            var startMenuPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
            };

            foreach (var startMenuPath in startMenuPaths)
            {
                if (Directory.Exists(startMenuPath))
                {
                    var result = await SearchShortcutsInDirectory(startMenuPath, appName);
                    if (!string.IsNullOrEmpty(result)) return result;
                }
            }
            return null;
        }

        private async Task<string> SearchShortcutsInDirectory(string directory, string appName)
        {
            try
            {
                var lnkFiles = Directory.GetFiles(directory, "*.lnk", SearchOption.AllDirectories);
                foreach (var lnkFile in lnkFiles)
                {
                    var shortcutName = Path.GetFileNameWithoutExtension(lnkFile);
                    if (shortcutName.ToLower().Contains(appName.ToLower()) ||
                        appName.ToLower().Contains(shortcutName.ToLower()))
                    {
                        var targetPath = GetShortcutTarget(lnkFile);
                        if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath) && targetPath.EndsWith(".exe"))
                        {
                            return targetPath;
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
        /// Yaygın program dizinlerinde yüzeysel arama
        /// </summary>
        private async Task<string> SearchInCommonProgramDirectoriesAsync(string appName)
        {
            var commonDirs = new[]
            {
                @"C:\Program Files",
                @"C:\Program Files (x86)",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps")
            };

            foreach (var dir in commonDirs)
            {
                if (Directory.Exists(dir))
                {
                    try
                    {
                        var subdirs = Directory.GetDirectories(dir);
                        foreach (var subdir in subdirs)
                        {
                            var dirName = Path.GetFileName(subdir);
                            if (dirName.ToLower().Contains(appName.ToLower()) ||
                                appName.ToLower().Contains(dirName.ToLower()))
                            {
                                var exeFiles = Directory.GetFiles(subdir, "*.exe", SearchOption.TopDirectoryOnly);
                                var mainExe = FindBestExecutable(exeFiles, appName);
                                if (!string.IsNullOrEmpty(mainExe))
                                {
                                    return mainExe;
                                }
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

        /// <summary>
        /// Detaylı arama - cache'de bulunamazsa kullanılır
        /// </summary>
        private async Task<string> PerformDetailedSearchAsync(string appName)
        {
            // Tüm program dizinlerinde derin arama
            var allProgramDirs = GetAllProgramDirectories();
            foreach (var programDir in allProgramDirs)
            {
                if (Directory.Exists(programDir))
                {
                    try
                    {
                        var result = await SearchInDirectoryDeep(programDir, appName);
                        if (!string.IsNullOrEmpty(result)) return result;
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            // Gaming platforms'larda ara
            var gamingResult = await SearchInGamingPlatformsAsync(appName);
            if (!string.IsNullOrEmpty(gamingResult)) return gamingResult;

            return null;
        }

        private async Task<string> SearchInDirectoryDeep(string directory, string appName)
        {
            try
            {
                var subdirs = Directory.GetDirectories(directory);
                foreach (var subdir in subdirs)
                {
                    var dirName = Path.GetFileName(subdir);
                    if (dirName.ToLower().Contains(appName.ToLower()) ||
                        appName.ToLower().Contains(dirName.ToLower()))
                    {
                        var exeFiles = Directory.GetFiles(subdir, "*.exe", SearchOption.AllDirectories);
                        var mainExe = FindBestExecutable(exeFiles, appName);
                        if (!string.IsNullOrEmpty(mainExe))
                        {
                            return mainExe;
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

        private async Task<string> SearchInGamingPlatformsAsync(string appName)
        {
            // Steam'de ara
            var steamResult = await SearchInSteamAsync(appName);
            if (!string.IsNullOrEmpty(steamResult)) return steamResult;

            // Epic Games'de ara
            var epicResult = await SearchInEpicGamesAsync(appName);
            if (!string.IsNullOrEmpty(epicResult)) return epicResult;

            return null;
        }

        private async Task<string> SearchInSteamAsync(string appName)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                var steamPath = key?.GetValue("SteamPath")?.ToString()?.Replace('/', '\\');
                if (!string.IsNullOrEmpty(steamPath) && Directory.Exists(steamPath))
                {
                    var steamAppsPath = Path.Combine(steamPath, "steamapps", "common");
                    if (Directory.Exists(steamAppsPath))
                    {
                        return await SearchInDirectoryDeep(steamAppsPath, appName);
                    }
                }
            }
            catch { }
            return null;
        }

        private async Task<string> SearchInEpicGamesAsync(string appName)
        {
            var manifestsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");

            if (Directory.Exists(manifestsPath))
            {
                var manifestFiles = Directory.GetFiles(manifestsPath, "*.item");
                foreach (var manifestFile in manifestFiles)
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(manifestFile);
                        var installLocationMatch = Regex.Match(
                            content, @"""InstallLocation"":\s*""([^""]+)""");
                        if (installLocationMatch.Success)
                        {
                            var installLocation = installLocationMatch.Groups[1].Value.Replace("\\\\", "\\");
                            if (Directory.Exists(installLocation))
                            {
                                var dirName = Path.GetFileName(installLocation);
                                if (dirName.ToLower().Contains(appName.ToLower()) ||
                                    appName.ToLower().Contains(dirName.ToLower()))
                                {
                                    var exeFiles = Directory.GetFiles(installLocation, "*.exe", SearchOption.AllDirectories);
                                    var mainExe = FindBestExecutable(exeFiles, appName);
                                    if (!string.IsNullOrEmpty(mainExe))
                                    {
                                        return mainExe;
                                    }
                                }
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

        // Cache yenileme metodları (eski halini koruyalım ama kullanım sıklığını azaltalım)
        private async Task RefreshApplicationCacheAsync()
        {
            _applicationCache.Clear();

            // Sadece temel cache'leme yapalım (performans için)
            await CacheRunningProcessesAsync();
            await CacheRegistryApplicationsAsync();
            await CacheStartMenuApplicationsAsync();

            _lastCacheUpdate = DateTime.Now;
        }

        private async Task CacheRunningProcessesAsync()
        {
            var processes = Process.GetProcesses();
            foreach (var process in processes)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        var name = Path.GetFileNameWithoutExtension(path);
                        _applicationCache.TryAdd(name.ToLower(), path);
                    }
                }
                catch
                {
                    continue;
                }
            }
        }

        private async Task CacheRegistryApplicationsAsync()
        {
            var registryPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"
            };

            foreach (var registryPath in registryPaths)
            {
                await ProcessRegistryPath(Registry.LocalMachine, registryPath);
                await ProcessRegistryPath(Registry.CurrentUser, registryPath);
            }
        }

        private async Task ProcessRegistryPath(RegistryKey baseKey, string subKeyPath)
        {
            try
            {
                using var key = baseKey.OpenSubKey(subKeyPath);
                if (key == null) return;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = key.OpenSubKey(subKeyName);
                        if (subKey == null) continue;

                        var displayName = subKey.GetValue("DisplayName")?.ToString();
                        var installLocation = subKey.GetValue("InstallLocation")?.ToString();
                        var exePath = subKey.GetValue("")?.ToString(); // Default value for App Paths

                        // App Paths için
                        if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                        {
                            var name = Path.GetFileNameWithoutExtension(exePath);
                            _applicationCache.TryAdd(name.ToLower(), exePath);
                        }

                        // Uninstall entries için
                        if (!string.IsNullOrEmpty(displayName))
                        {
                            var appName = displayName.ToLower();

                            if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                            {
                                var exeFiles = Directory.GetFiles(installLocation, "*.exe", SearchOption.AllDirectories);
                                var mainExe = FindBestExecutable(exeFiles, displayName);
                                if (!string.IsNullOrEmpty(mainExe))
                                {
                                    _applicationCache.TryAdd(appName, mainExe);
                                }
                            }
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
            catch
            {
                // Registry access denied
            }
        }

        private async Task CacheStartMenuApplicationsAsync()
        {
            var startMenuPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
            };

            foreach (var startMenuPath in startMenuPaths)
            {
                if (Directory.Exists(startMenuPath))
                {
                    await ProcessShortcutDirectory(startMenuPath);
                }
            }
        }

        private async Task ProcessShortcutDirectory(string directory)
        {
            try
            {
                var lnkFiles = Directory.GetFiles(directory, "*.lnk", SearchOption.AllDirectories);
                foreach (var lnkFile in lnkFiles)
                {
                    var shortcutName = Path.GetFileNameWithoutExtension(lnkFile);
                    var targetPath = GetShortcutTarget(lnkFile);

                    if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath) && targetPath.EndsWith(".exe"))
                    {
                        _applicationCache.TryAdd(shortcutName.ToLower(), targetPath);
                    }
                }
            }
            catch
            {
                // Access denied or other errors
            }
        }

        private List<string> GetAllProgramDirectories()
        {
            var directories = new List<string>();
            var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady);

            var commonDirNames = new[] { "Program Files", "Program Files (x86)", "Programs", "Games", "Apps" };

            foreach (var drive in drives)
            {
                foreach (var dirName in commonDirNames)
                {
                    var fullPath = Path.Combine(drive.RootDirectory.FullName, dirName);
                    if (Directory.Exists(fullPath))
                    {
                        directories.Add(fullPath);
                    }
                }
            }

            // User-specific directories
            directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));
            directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps"));

            return directories;
        }

        private string FindBestExecutable(string[] exeFiles, string appName)
        {
            if (exeFiles.Length == 0) return null;

            // Önce isimle eşleşen exe'yi ara
            var nameMatch = exeFiles.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).ToLower().Contains(appName.ToLower()));
            if (!string.IsNullOrEmpty(nameMatch))
            {
                return nameMatch;
            }

            // Launcher, setup, uninstall gibi kelimeleri içermeyenleri filtrele
            var filteredExes = exeFiles.Where(f =>
            {
                var fileName = Path.GetFileNameWithoutExtension(f).ToLower();
                return !fileName.Contains("launcher") &&
                       !fileName.Contains("setup") &&
                       !fileName.Contains("uninstall") &&
                       !fileName.Contains("crash") &&
                       !fileName.Contains("report") &&
                       !fileName.Contains("update");
            }).ToArray();

            if (filteredExes.Length > 0)
            {
                // En büyük exe dosyasını seç (genelde ana executable)
                return filteredExes.OrderByDescending(f => new FileInfo(f).Length).First();
            }

            return exeFiles.OrderByDescending(f => new FileInfo(f).Length).First();
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

            var paths = pathVariable.Split(';');
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        var files = Directory.GetFiles(path, "*.exe");
                        var match = files.FirstOrDefault(f =>
                            Path.GetFileNameWithoutExtension(f).ToLower().Contains(appName.ToLower()));
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

        private async Task<string> SearchInDesktopAsync(string appName)
        {
            var desktopPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            };

            foreach (var desktopPath in desktopPaths)
            {
                if (Directory.Exists(desktopPath))
                {
                    var lnkFiles = Directory.GetFiles(desktopPath, "*.lnk");
                    foreach (var lnkFile in lnkFiles)
                    {
                        var shortcutName = Path.GetFileNameWithoutExtension(lnkFile);
                        if (shortcutName.ToLower().Contains(appName.ToLower()))
                        {
                            var targetPath = GetShortcutTarget(lnkFile);
                            if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
                            {
                                return targetPath;
                            }
                        }
                    }
                }
            }
            return null;
        }

        private string GetShortcutTarget(string lnkPath)
        {
            try
            {
                // WScript.Shell kullan
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic shell = Activator.CreateInstance(shellType);
                    var shortcut = shell.CreateShortcut(lnkPath);
                    return shortcut.TargetPath;
                }
            }
            catch
            {
                // Başarısız olursa null döndür
            }
            return null;
        }
        

        private string GetApplicationDisplayName(string executablePath, string fallbackName)
        {
            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
                return !string.IsNullOrEmpty(versionInfo.ProductName)
                    ? versionInfo.ProductName
                    : Path.GetFileNameWithoutExtension(executablePath);
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
                var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
                return versionInfo.FileVersion ?? versionInfo.ProductVersion;
            }
            catch
            {
                return null;
            }
        }

        private DateTime? GetApplicationInstallDate(string executablePath)
        {
            try
            {
                var fileInfo = new FileInfo(executablePath);
                return fileInfo.CreationTime;
            }
            catch
            {
                return null;
            }
        }
        /// <summary>
        /// Checks whether the given application is installed.
        /// </summary>
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
    }
}