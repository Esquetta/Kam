using SmartVoiceAgent.Core.Dtos;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SmartVoiceAgent.Infrastructure.Helpers
{
    /// <summary>
    /// Provides methods to retrieve information about the currently active window on Windows OS.
    /// </summary>
    public static class WindowHelper
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        /// <summary>
        /// Gets information about the currently active window, including title and process details.
        /// </summary>
        /// <returns>Returns an <see cref="ActiveWindowInfo"/> if available; otherwise null.</returns>
        public static ActiveWindowInfo? GetActiveWindow()
        {
            try
            {
                var hWnd = GetForegroundWindow();
                if (hWnd == IntPtr.Zero)
                    return null;

                // Window Title
                var builder = new StringBuilder(512);
                _ = GetWindowText(hWnd, builder, builder.Capacity);
                var title = builder.ToString();

                // Process Info
                _ = GetWindowThreadProcessId(hWnd, out uint processId);
                string processName = "Unknown";
                string exePath = string.Empty;

                try
                {
                    var process = Process.GetProcessById((int)processId);
                    processName = process.ProcessName;
                    exePath = process.MainModule?.FileName ?? string.Empty;
                }
                catch (Exception ex) when (
                    ex is ArgumentException ||
                    ex is System.ComponentModel.Win32Exception ||
                    ex is InvalidOperationException)
                {
                    // Bazı sistem veya izinli processlere erişilemeyebilir
                    processName = "AccessDenied";
                    exePath = string.Empty;
                }

                return new ActiveWindowInfo
                {
                    Title = title,
                    ProcessId = (int)processId,
                    ProcessName = processName,
                    ExecutablePath = exePath
                };
            }
            catch (Exception ex)
            {
                // TODO: LoggerServiceBase ile logla
                Console.Error.WriteLine($"[WindowHelper] Failed to get active window. {ex.Message}");
                return null;
            }
        }
    }
}
