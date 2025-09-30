using Core.CrossCuttingConcerns.Logging.Serilog;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Interfaces;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace SmartVoiceAgent.Infrastructure.Services;

public class ActiveWindowService : IActiveWindowService
{
    private readonly LoggerServiceBase _logger;

    public ActiveWindowService(LoggerServiceBase logger)
    {
        _logger = logger;
    }

    #region Win32 API Declarations

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint GW_OWNER = 4;

    #endregion

    /// <inheritdoc />
    public async Task<ActiveWindowInfo> GetActiveWindowInfoAsync()
    {
        try
        {
            return await Task.Run(() =>
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                {
                    _logger.Warn("No foreground window found");
                    return null;
                }

                var windowInfo = GetWindowInfo(hwnd);

                if (windowInfo != null)
                {
                    _logger.Info($"Active window: {windowInfo.Title} (PID: {windowInfo.ProcessId})");
                }

                return windowInfo;
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"Error getting active window info: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ActiveWindowInfo>> GetAllWindowsAsync()
    {
        try
        {
            return await Task.Run(() =>
            {
                var windows = new List<ActiveWindowInfo>();

                EnumWindows((hWnd, lParam) =>
                {
                    // Filter out invisible windows and tool windows
                    if (IsWindowVisible(hWnd) && !IsToolWindow(hWnd))
                    {
                        var windowInfo = GetWindowInfo(hWnd);
                        if (windowInfo != null && !string.IsNullOrWhiteSpace(windowInfo.Title))
                        {
                            windows.Add(windowInfo);
                        }
                    }
                    return true; // Continue enumeration
                }, IntPtr.Zero);

                _logger.Info($"Found {windows.Count} visible windows");
                return windows.AsEnumerable();
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"Error getting all windows: {ex.Message}");
            return Enumerable.Empty<ActiveWindowInfo>();
        }
    }

    /// <summary>
    /// Gets window information for a specific window handle
    /// </summary>
    private ActiveWindowInfo GetWindowInfo(IntPtr hWnd)
    {
        try
        {
            // Window title
            int titleLength = GetWindowTextLength(hWnd);
            if (titleLength == 0)
            {
                return null;
            }

            var titleBuilder = new StringBuilder(titleLength + 1);
            GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);
            string title = titleBuilder.ToString();

            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            // Process information
            GetWindowThreadProcessId(hWnd, out uint processId);

            string processName = "Unknown";
            string executablePath = "Unknown";

            try
            {
                using var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;

                try
                {
                    executablePath = process.MainModule?.FileName ?? "Unknown";
                }
                catch (Win32Exception ex)
                {
                    // Access denied for some system processes
                    executablePath = $"Access Denied (PID: {processId})";
                }
            }
            catch (ArgumentException)
            {
                // Process has already exited
                _logger.Debug($"Process {processId} has already exited");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Could not get process info for PID {processId}: {ex.Message}");
            }

            // Window bounds
            if (!GetWindowRect(hWnd, out RECT rect))
            {
                return null;
            }

            var windowBounds = new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);

            // Window state
            bool isMinimized = IsIconic(hWnd);
            bool isMaximized = IsZoomed(hWnd);
            bool isVisible = IsWindowVisible(hWnd);

            return new ActiveWindowInfo
            {
                Title = title,
                ProcessId = (int)processId,
                ProcessName = processName,
                ExecutablePath = executablePath,
                WindowBounds = windowBounds,
                IsMaximized = isMaximized,
                IsMinimized = isMinimized,
                IsVisible = isVisible
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"Error getting window info for handle {hWnd}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Checks if a window is a tool window (should be filtered out)
    /// </summary>
    private bool IsToolWindow(IntPtr hWnd)
    {
        try
        {
            // Check if window has owner
            if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero)
            {
                return true;
            }

            // Check extended style
            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOOLWINDOW) != 0)
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets window class name
    /// </summary>
    private string GetWindowClassName(IntPtr hWnd)
    {
        try
        {
            var className = new StringBuilder(256);
            GetClassName(hWnd, className, className.Capacity);
            return className.ToString();
        }
        catch
        {
            return "Unknown";
        }
    }
}