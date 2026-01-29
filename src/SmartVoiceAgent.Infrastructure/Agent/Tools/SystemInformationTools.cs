using AgentFrameworkToolkit.Tools;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SmartVoiceAgent.Infrastructure.Agent.Tools
{
    /// <summary>
    /// System information tools for monitoring CPU, memory, disk, and battery status.
    /// Cross-platform support for Windows, macOS, and Linux.
    /// </summary>
    public sealed class SystemInformationTools
    {
        #region Windows APIs for Battery
        [StructLayout(LayoutKind.Sequential)]
        private class SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte Reserved1;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(SYSTEM_POWER_STATUS lpSystemPowerStatus);
        #endregion

        /// <summary>
        /// Gets comprehensive system information including CPU, memory, and disk.
        /// </summary>
        [AITool("get_system_info", "Gets comprehensive system information including CPU, memory, and disk usage.")]
        public Task<string> GetSystemInfoAsync()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("🖥️ System Information");
                sb.AppendLine("═══════════════════════════════════════");

                // OS Information
                sb.AppendLine($"Operating System: {RuntimeInformation.OSDescription}");
                sb.AppendLine($"Architecture: {RuntimeInformation.OSArchitecture}");
                sb.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");
                sb.AppendLine();

                // CPU Information
                sb.AppendLine(GetCpuInfo());
                sb.AppendLine();

                // Memory Information
                sb.AppendLine(GetMemoryInfo());
                sb.AppendLine();

                // Disk Information
                sb.AppendLine(GetDiskInfo());

                return Task.FromResult(sb.ToString());
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Failed to get system info: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets CPU usage and information.
        /// </summary>
        [AITool("get_cpu_info", "Gets CPU usage percentage and core information.")]
        public Task<string> GetCpuInfoAsync()
        {
            try
            {
                return Task.FromResult(GetCpuInfo());
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Failed to get CPU info: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets memory usage information.
        /// </summary>
        [AITool("get_memory_info", "Gets RAM usage and availability.")]
        public Task<string> GetMemoryInfoAsync()
        {
            try
            {
                return Task.FromResult(GetMemoryInfo());
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Failed to get memory info: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets disk space information for all drives.
        /// </summary>
        [AITool("get_disk_info", "Gets disk space usage for all drives.")]
        public Task<string> GetDiskInfoAsync()
        {
            try
            {
                return Task.FromResult(GetDiskInfo());
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Failed to get disk info: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets battery status for laptops.
        /// </summary>
        [AITool("get_battery_status", "Gets battery status including charge level and power source.")]
        public Task<string> GetBatteryStatusAsync()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return Task.FromResult(GetBatteryStatusWindows());
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return Task.FromResult(GetBatteryStatusMacOS());
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return Task.FromResult(GetBatteryStatusLinux());
                }
                else
                {
                    return Task.FromResult("❌ Battery status not supported on this platform.");
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Failed to get battery status: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the list of running processes.
        /// </summary>
        [AITool("list_processes", "Lists the top running processes by CPU or memory usage.")]
        public Task<string> ListProcessesAsync(
            [Description("Sort by: 'cpu' or 'memory'")] string sortBy = "memory",
            [Description("Number of processes to return")] int count = 10)
        {
            try
            {
                var processes = Process.GetProcesses()
                    .Where(p => !p.HasExited)
                    .Select(p => new
                    {
                        p.ProcessName,
                        p.Id,
                        MemoryMB = p.WorkingSet64 / 1024 / 1024,
                        p.TotalProcessorTime
                    });

                processes = sortBy.ToLower() switch
                {
                    "cpu" => processes.OrderByDescending(p => p.TotalProcessorTime.TotalSeconds),
                    _ => processes.OrderByDescending(p => p.MemoryMB)
                };

                var topProcesses = processes.Take(count).ToList();

                var sb = new StringBuilder();
                sb.AppendLine($"📊 Top {count} Processes (sorted by {sortBy})");
                sb.AppendLine("═══════════════════════════════════════");
                sb.AppendLine(string.Format("{0,-20} {1,-8} {2,-12}", "Name", "PID", "Memory (MB)"));
                sb.AppendLine(new string('-', 50));

                foreach (var proc in topProcesses)
                {
                    sb.AppendLine($"{proc.ProcessName,-20} {proc.Id,-8} {proc.MemoryMB,-12:N0}");
                }

                return Task.FromResult(sb.ToString());
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Failed to list processes: {ex.Message}");
            }
        }

        /// <summary>
        /// Kills a process by name or ID.
        /// </summary>
        [AITool("kill_process", "Terminates a process by name or ID.")]
        public Task<string> KillProcessAsync(
            [Description("Process name or ID to kill")] string processNameOrId,
            [Description("Force kill immediately")] bool force = false)
        {
            try
            {
                Process? process;

                if (int.TryParse(processNameOrId, out int processId))
                {
                    process = Process.GetProcessById(processId);
                }
                else
                {
                    var processes = Process.GetProcessesByName(processNameOrId);
                    if (processes.Length == 0)
                    {
                        return Task.FromResult($"❌ No process found with name '{processNameOrId}'.");
                    }
                    if (processes.Length > 1)
                    {
                        return Task.FromResult($"⚠️ Multiple processes found with name '{processNameOrId}'. Use PID instead.\n" +
                            $"Found: {string.Join(", ", processes.Select(p => $"{p.ProcessName} (PID: {p.Id})"))}");
                    }
                    process = processes[0];
                }

                var name = process.ProcessName;
                var id = process.Id;

                if (force)
                {
                    process.Kill();
                }
                else
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(5000))
                    {
                        process.Kill();
                    }
                }

                return Task.FromResult($"✅ Process '{name}' (PID: {id}) terminated successfully.");
            }
            catch (ArgumentException)
            {
                return Task.FromResult($"❌ Process with ID '{processNameOrId}' not found.");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Failed to kill process: {ex.Message}");
            }
        }

        #region Helper Methods

        private static string GetCpuInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("🔄 CPU Information");
            sb.AppendLine($"  Processor Count: {Environment.ProcessorCount}");

            // Get CPU usage using Process
            var currentProcess = Process.GetCurrentProcess();
            var startTime = DateTime.UtcNow;
            var startCpuUsage = currentProcess.TotalProcessorTime;

            System.Threading.Thread.Sleep(500); // Short delay for measurement

            var endTime = DateTime.UtcNow;
            var endCpuUsage = currentProcess.TotalProcessorTime;

            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;
            var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

            sb.AppendLine($"  Current Usage: {cpuUsageTotal * 100:F1}%");

            return sb.ToString();
        }

        private static string GetMemoryInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("💾 Memory Information");

            var proc = Process.GetCurrentProcess();
            var workingSet = proc.WorkingSet64;
            var gcMemory = GC.GetTotalMemory(false);

            sb.AppendLine($"  Working Set: {FormatBytes(workingSet)}");
            sb.AppendLine($"  GC Memory: {FormatBytes(gcMemory)}");

            // Try to get total system memory
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var memInfo = File.ReadAllText("/proc/meminfo");
                    var totalLine = memInfo.Split('\n').FirstOrDefault(l => l.StartsWith("MemTotal:"));
                    if (totalLine != null)
                    {
                        var totalKb = long.Parse(totalLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[1]);
                        sb.AppendLine($"  Total System Memory: {FormatBytes(totalKb * 1024)}");
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var psi = new ProcessStartInfo("sysctl", "-n hw.memsize")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false
                    };
                    var process = Process.Start(psi);
                    var output = process?.StandardOutput.ReadToEnd();
                    process?.WaitForExit();
                    if (long.TryParse(output?.Trim(), out var totalBytes))
                    {
                        sb.AppendLine($"  Total System Memory: {FormatBytes(totalBytes)}");
                    }
                }
            }
            catch { /* Ignore errors getting total memory */ }

            return sb.ToString();
        }

        private static string GetDiskInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("💿 Disk Information");

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var usedSpace = drive.TotalSize - drive.AvailableFreeSpace;
                var percentUsed = (double)usedSpace / drive.TotalSize * 100;

                sb.AppendLine($"  Drive {drive.Name}");
                sb.AppendLine($"    Total: {FormatBytes(drive.TotalSize)}");
                sb.AppendLine($"    Used: {FormatBytes(usedSpace)} ({percentUsed:F1}%)");
                sb.AppendLine($"    Free: {FormatBytes(drive.AvailableFreeSpace)}");
            }

            return sb.ToString();
        }

        private static string GetBatteryStatusWindows()
        {
            var status = new SYSTEM_POWER_STATUS();
            if (!GetSystemPowerStatus(status))
            {
                return "❌ Failed to get battery status (may not be a laptop).";
            }

            var sb = new StringBuilder();
            sb.AppendLine("🔋 Battery Status");

            // AC Line Status
            sb.AppendLine($"  Power Source: {(status.ACLineStatus == 1 ? "⚡ AC Power" : "🔋 Battery")}");

            // Battery Life Percent
            if (status.BatteryLifePercent != 255)
            {
                var emoji = status.BatteryLifePercent switch
                {
                    > 75 => "🟢",
                    > 50 => "🟡",
                    > 25 => "🟠",
                    _ => "🔴"
                };
                sb.AppendLine($"  Charge Level: {emoji} {status.BatteryLifePercent}%");
            }

            // Battery Life Time
            if (status.BatteryLifeTime != -1)
            {
                var timeSpan = TimeSpan.FromSeconds(status.BatteryLifeTime);
                sb.AppendLine($"  Time Remaining: {timeSpan.Hours}h {timeSpan.Minutes}m");
            }

            // Battery Flag
            var flag = status.BatteryFlag;
            if (flag != 255)
            {
                var charging = (flag & 8) != 0;
                var critical = (flag & 4) != 0;
                var low = (flag & 2) != 0;
                var high = (flag & 1) != 0;

                if (charging) sb.AppendLine("  Status: ⚡ Charging");
                if (critical) sb.AppendLine("  Warning: 🔴 Critical Battery!");
                else if (low) sb.AppendLine("  Warning: 🟠 Low Battery");
            }

            return sb.ToString();
        }

        private static string GetBatteryStatusMacOS()
        {
            try
            {
                var psi = new ProcessStartInfo("pmset", "-g batt")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                var process = Process.Start(psi);
                var output = process?.StandardOutput.ReadToEnd();
                process?.WaitForExit();

                if (!string.IsNullOrEmpty(output))
                {
                    return $"🔋 Battery Status:\n```\n{output}\n```";
                }
            }
            catch { }

            return "❌ Failed to get battery status.";
        }

        private static string GetBatteryStatusLinux()
        {
            try
            {
                // Try to read from sysfs
                var powerSupplyPath = "/sys/class/power_supply/";
                if (!Directory.Exists(powerSupplyPath))
                    return "❌ Battery information not available.";

                var batteries = Directory.GetDirectories(powerSupplyPath, "BAT*");
                if (batteries.Length == 0)
                    return "❌ No battery found (may be a desktop).";

                var sb = new StringBuilder();
                sb.AppendLine("🔋 Battery Status");

                foreach (var battery in batteries)
                {
                    var name = Path.GetFileName(battery);
                    var capacity = ReadSysfsValue(Path.Combine(battery, "capacity"));
                    var status = ReadSysfsValue(Path.Combine(battery, "status"));

                    sb.AppendLine($"  {name}: {capacity}% - {status}");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"❌ Failed to get battery status: {ex.Message}";
            }
        }

        private static string? ReadSysfsValue(string path)
        {
            try
            {
                return File.ReadAllText(path).Trim();
            }
            catch
            {
                return null;
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        #endregion

        public IEnumerable<AIFunction> GetTools()
        {
            return
            [
                AIFunctionFactory.Create(GetSystemInfoAsync),
                AIFunctionFactory.Create(GetCpuInfoAsync),
                AIFunctionFactory.Create(GetMemoryInfoAsync),
                AIFunctionFactory.Create(GetDiskInfoAsync),
                AIFunctionFactory.Create(GetBatteryStatusAsync),
                AIFunctionFactory.Create(ListProcessesAsync),
                AIFunctionFactory.Create(KillProcessAsync)
            ];
        }
    }
}
