using AgentFrameworkToolkit.Tools;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SmartVoiceAgent.Infrastructure.Agent.Tools
{
    /// <summary>
    /// Clipboard tools for reading and writing clipboard content.
    /// Cross-platform support for Windows, macOS, and Linux.
    /// </summary>
    public sealed class ClipboardTools
    {
        #region Windows APIs
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        private const uint CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 0x0002;
        #endregion

        /// <summary>
        /// Gets the current text content from the clipboard.
        /// </summary>
        [AITool("get_clipboard", "Gets the current text content from the clipboard.")]
        public Task<string> GetClipboardAsync(
            [Description("Maximum characters to return (0 for all)")]
            int maxLength = 0)
        {
            try
            {
                string content;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    content = GetClipboardWindows();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    content = GetClipboardMacOS();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    content = GetClipboardLinux();
                }
                else
                {
                    return Task.FromResult("❌ Clipboard access not supported on this platform.");
                }

                if (string.IsNullOrEmpty(content))
                {
                    return Task.FromResult("📋 Clipboard is empty.");
                }

                if (maxLength > 0 && content.Length > maxLength)
                {
                    content = content[..maxLength] + $"\n... ({content.Length - maxLength} more characters)";
                }

                return Task.FromResult($"📋 Clipboard content:\n```\n{content}\n```");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Failed to read clipboard: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the clipboard text content.
        /// </summary>
        [AITool("set_clipboard", "Sets the clipboard text content.")]
        public Task<string> SetClipboardAsync(
            [Description("Text content to set in the clipboard")]
            string content)
        {
            try
            {
                if (string.IsNullOrEmpty(content))
                {
                    return Task.FromResult("❌ Cannot set empty clipboard content.");
                }

                bool success;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    success = SetClipboardWindows(content);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    success = SetClipboardMacOS(content);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    success = SetClipboardLinux(content);
                }
                else
                {
                    return Task.FromResult("❌ Clipboard access not supported on this platform.");
                }

                if (success)
                {
                    var preview = content.Length > 100 ? content[..100] + "..." : content;
                    return Task.FromResult($"✅ Clipboard set successfully.\nPreview: `{preview}`");
                }
                else
                {
                    return Task.FromResult("❌ Failed to set clipboard content.");
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Failed to set clipboard: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears the clipboard content.
        /// </summary>
        [AITool("clear_clipboard", "Clears the clipboard content.")]
        public Task<string> ClearClipboardAsync()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    if (OpenClipboard(IntPtr.Zero))
                    {
                        EmptyClipboard();
                        CloseClipboard();
                        return Task.FromResult("✅ Clipboard cleared successfully.");
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // On macOS, set empty string
                    SetClipboardMacOS("");
                    return Task.FromResult("✅ Clipboard cleared successfully.");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    SetClipboardLinux("");
                    return Task.FromResult("✅ Clipboard cleared successfully.");
                }

                return Task.FromResult("❌ Clipboard clear not supported on this platform.");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"❌ Failed to clear clipboard: {ex.Message}");
            }
        }

        #region Platform-Specific Implementations

        private static string GetClipboardWindows()
        {
            if (!OpenClipboard(IntPtr.Zero))
                throw new InvalidOperationException("Failed to open clipboard");

            try
            {
                var hData = GetClipboardData(CF_UNICODETEXT);
                if (hData == IntPtr.Zero)
                    return string.Empty;

                var ptr = GlobalLock(hData);
                if (ptr == IntPtr.Zero)
                    return string.Empty;

                try
                {
                    return Marshal.PtrToStringUni(ptr) ?? string.Empty;
                }
                finally
                {
                    GlobalUnlock(hData);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }

        private static bool SetClipboardWindows(string text)
        {
            if (!OpenClipboard(IntPtr.Zero))
                return false;

            try
            {
                EmptyClipboard();

                var bytes = (text.Length + 1) * 2;
                var hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
                if (hMem == IntPtr.Zero)
                    return false;

                var ptr = GlobalLock(hMem);
                if (ptr == IntPtr.Zero)
                    return false;

                try
                {
                    Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                    Marshal.WriteInt16(ptr, text.Length * 2, 0); // Null terminator
                }
                finally
                {
                    GlobalUnlock(hMem);
                }

                return SetClipboardData(CF_UNICODETEXT, hMem) != IntPtr.Zero;
            }
            finally
            {
                CloseClipboard();
            }
        }

        private static string GetClipboardMacOS()
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pbpaste",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output;
        }

        private static bool SetClipboardMacOS(string text)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pbcopy",
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.StandardInput.Write(text);
            process.StandardInput.Close();
            process.WaitForExit();
            return process.ExitCode == 0;
        }

        private static string GetClipboardLinux()
        {
            // Try wl-copy/wl-paste first (Wayland), then xclip (X11)
            string[] commands = { "wl-paste", "xclip -selection clipboard -o" };

            foreach (var cmd in commands)
            {
                try
                {
                    var parts = cmd.Split(' ');
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = parts[0],
                            Arguments = string.Join(" ", parts.Skip(1)),
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode == 0)
                        return output;
                }
                catch { /* Try next command */ }
            }

            return string.Empty;
        }

        private static bool SetClipboardLinux(string text)
        {
            string[] commands = { "wl-copy", "xclip -selection clipboard" };

            foreach (var cmd in commands)
            {
                try
                {
                    var parts = cmd.Split(' ');
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = parts[0],
                            Arguments = string.Join(" ", parts.Skip(1)),
                            RedirectStandardInput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    process.StandardInput.Write(text);
                    process.StandardInput.Close();
                    process.WaitForExit();
                    if (process.ExitCode == 0)
                        return true;
                }
                catch { /* Try next command */ }
            }

            return false;
        }

        #endregion

        public IEnumerable<AIFunction> GetTools()
        {
            return
            [
                AIFunctionFactory.Create(GetClipboardAsync),
                AIFunctionFactory.Create(SetClipboardAsync),
                AIFunctionFactory.Create(ClearClipboardAsync)
            ];
        }
    }
}
