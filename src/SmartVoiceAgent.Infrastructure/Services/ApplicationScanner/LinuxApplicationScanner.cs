using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Interfaces;
using System.Diagnostics;

namespace SmartVoiceAgent.Infrastructure.Services.ApplicationScanner;
/// <summary>
/// Linux-specific application scanner implementation.
/// </summary>
public class LinuxApplicationScanner : IApplicationScanner
{
    private readonly string[] _applicationPaths = {
        "/usr/share/applications",
        "/usr/local/share/applications",
        "~/.local/share/applications"
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
                    var desktopFiles = Directory.GetFiles(expandedPath, "*.desktop");

                    foreach (var desktopFile in desktopFiles)
                    {
                        try
                        {
                            var appInfo = ParseDesktopFile(desktopFile);
                            if (appInfo != null)
                            {
                                var isRunning = IsApplicationRunning(appInfo.Name, appInfo.Path, runningProcesses);
                                apps.Add(appInfo with { IsRunning = isRunning });
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error parsing desktop file {desktopFile}: {ex.Message}");
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

    private AppInfoDTO ParseDesktopFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        string name = null;
        string exec = null;
        bool noDisplay = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("Name="))
                name = line.Substring(5).Trim();
            else if (line.StartsWith("Exec="))
                exec = line.Substring(5).Trim();
            else if (line.StartsWith("NoDisplay=true"))
                noDisplay = true;
        }

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(exec) || noDisplay)
            return null;

        // Clean up exec command (remove arguments)
        var execParts = exec.Split(' ');
        var executablePath = execParts[0];

        // Resolve full path if it's just a command name
        if (!executablePath.StartsWith("/"))
        {
            executablePath = FindExecutableInPath(executablePath);
        }

        return new AppInfoDTO(name, executablePath ?? exec, false);
    }

    private string FindExecutableInPath(string command)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;

        var paths = pathEnv.Split(':');
        foreach (var path in paths)
        {
            var fullPath = Path.Combine(path, command);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
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

    private bool IsApplicationRunning(string appName, string executablePath, HashSet<string> runningProcesses)
    {
        if (string.IsNullOrEmpty(executablePath))
            return false;

        var fileName = Path.GetFileNameWithoutExtension(executablePath).ToLower();
        return runningProcesses.Contains(fileName);
    }
}