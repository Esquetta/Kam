using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Interfaces;
using System.Diagnostics;

namespace SmartVoiceAgent.Infrastructure.Services.ApplicationScanner;
/// <summary>
/// macOS-specific application scanner implementation.
/// </summary>
public class MacOSApplicationScanner : IApplicationScanner
{
    private readonly string[] _applicationPaths = {
        "/Applications",
        "/System/Applications",
        "~/Applications"
    };

    public async Task<IEnumerable<AppInfoDTO>> GetInstalledApplicationsAsync()
    {
        return await Task.Run(() =>
        {
            var apps = new List<AppInfoDTO>();
            var runningProcesses = GetRunningProcesses();

            foreach (var path in _applicationPaths)
            {
                var expandedPath = path.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

                if (!Directory.Exists(expandedPath)) continue;

                try
                {
                    var appBundles = Directory.GetDirectories(expandedPath, "*.app");

                    foreach (var appBundle in appBundles)
                    {
                        try
                        {
                            var appInfo = ParseAppBundle(appBundle);
                            if (appInfo != null)
                            {
                                var isRunning = IsApplicationRunning(appInfo.Name, appInfo.Path, runningProcesses);
                                apps.Add(appInfo with { IsRunning = isRunning });
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error parsing app bundle {appBundle}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error accessing directory {expandedPath}: {ex.Message}");
                }
            }

            return apps.DistinctBy(a => a.Name).OrderBy(a => a.Name);
        });
    }
    public async Task<ApplicationInstallInfo> FindApplicationAsync(string appName)
    {
        return await Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(appName))
                return new ApplicationInstallInfo(false, string.Empty, string.Empty);

            var appNameLower = appName.ToLower();

            foreach (var path in _applicationPaths)
            {
                var expandedPath = path.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

                if (!Directory.Exists(expandedPath)) continue;

                try
                {
                    var appBundles = Directory.GetDirectories(expandedPath, "*.app");

                    foreach (var appBundle in appBundles)
                    {
                        try
                        {
                            var appInfo = ParseAppBundle(appBundle);
                            if (appInfo != null && appInfo.Name.ToLower().Contains(appNameLower))
                            {
                                var infoPlistPath = Path.Combine(appBundle, "Contents", "Info.plist");
                                string version = null;
                                DateTime? installDate = null;

                                if (File.Exists(infoPlistPath))
                                {
                                    try
                                    {
                                        var plistContent = File.ReadAllText(infoPlistPath);
                                        version = ExtractPlistValue(plistContent, "CFBundleShortVersionString") ??
                                                 ExtractPlistValue(plistContent, "CFBundleVersion");

                                        var creationTime = Directory.GetCreationTime(appBundle);
                                        installDate = creationTime;
                                    }
                                    catch
                                    {
                                        // Ignore plist parsing errors
                                    }
                                }

                                return new ApplicationInstallInfo(
                                    true,
                                    appBundle,
                                    appInfo.Name,
                                    version,
                                    installDate
                                );
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error parsing app bundle {appBundle}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error accessing directory {expandedPath}: {ex.Message}");
                }
            }

            return new ApplicationInstallInfo(false, string.Empty, string.Empty);
        });
    }

    public async Task<string> GetApplicationPathAsync(string appName)
    {
        var appInfo = await FindApplicationAsync(appName);
        return appInfo.IsInstalled ? appInfo.ExecutablePath : null;
    }

    private AppInfoDTO ParseAppBundle(string bundlePath)
    {
        var infoPlistPath = Path.Combine(bundlePath, "Contents", "Info.plist");
        if (!File.Exists(infoPlistPath))
            return null;

        try
        {
            var plistContent = File.ReadAllText(infoPlistPath);
            var displayName = ExtractPlistValue(plistContent, "CFBundleDisplayName") ??
                             ExtractPlistValue(plistContent, "CFBundleName");

            if (string.IsNullOrEmpty(displayName))
                displayName = Path.GetFileNameWithoutExtension(bundlePath);

            return new AppInfoDTO(displayName, bundlePath, false);
        }
        catch
        {
            // Fallback to bundle name
            var displayName = Path.GetFileNameWithoutExtension(bundlePath);
            return new AppInfoDTO(displayName, bundlePath, false);
        }
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

    private HashSet<string> GetRunningProcesses()
    {
        var processes = new HashSet<string>();
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (!string.IsNullOrEmpty(process.ProcessName))
                    {
                        processes.Add(process.ProcessName.ToLower());
                    }
                }
                catch
                {
                    // Some processes may not be accessible
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting running processes: {ex.Message}");
        }
        return processes;
    }

    private bool IsApplicationRunning(string appName, string appPath, HashSet<string> runningProcesses)
    {
        var appNameLower = appName.ToLower();
        var bundleName = Path.GetFileNameWithoutExtension(appPath).ToLower();

        return runningProcesses.Contains(appNameLower) || runningProcesses.Contains(bundleName);
    }
}