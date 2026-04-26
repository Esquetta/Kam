using System.Runtime.InteropServices;
using System.Text;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Actions;

public sealed class DesktopAutomationAdapter : IDesktopAutomationAdapter
{
    private const uint KeyEventKeyUp = 0x0002;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;

    public Task<SkillActionStepResult> ClickAsync(
        int x,
        int y,
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Task.FromResult(Unsupported(SkillActionTypes.Click));
        }

        SetCursorPos(x, y);
        MouseEvent(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
        MouseEvent(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
        return Task.FromResult(SkillActionStepResult.Succeeded(
            SkillActionTypes.Click,
            $"Clicked at {x},{y}."));
    }

    public Task<SkillActionStepResult> TypeTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Task.FromResult(Unsupported(SkillActionTypes.TypeText));
        }

        foreach (var character in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = VkKeyScan(character);
            if (key == -1)
            {
                return Task.FromResult(SkillActionStepResult.Failed(
                    SkillActionTypes.TypeText,
                    $"Cannot type unsupported character '{character}'.",
                    "unsupported_character"));
            }

            var virtualKey = (byte)(key & 0xff);
            var shiftState = (key >> 8) & 0xff;
            if ((shiftState & 1) != 0)
            {
                KeyDown(0x10);
            }

            KeyDown(virtualKey);
            KeyUp(virtualKey);

            if ((shiftState & 1) != 0)
            {
                KeyUp(0x10);
            }
        }

        return Task.FromResult(SkillActionStepResult.Succeeded(
            SkillActionTypes.TypeText,
            $"Typed {text.Length} character(s)."));
    }

    public Task<SkillActionStepResult> HotkeyAsync(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Task.FromResult(Unsupported(SkillActionTypes.Hotkey));
        }

        var virtualKeys = new List<byte>();
        foreach (var key in keys)
        {
            var virtualKey = ResolveVirtualKey(key);
            if (virtualKey is null)
            {
                return Task.FromResult(SkillActionStepResult.Failed(
                    SkillActionTypes.Hotkey,
                    $"Unsupported hotkey key '{key}'.",
                    "unsupported_key"));
            }

            virtualKeys.Add(virtualKey.Value);
        }

        foreach (var virtualKey in virtualKeys)
        {
            KeyDown(virtualKey);
        }

        for (var index = virtualKeys.Count - 1; index >= 0; index--)
        {
            KeyUp(virtualKeys[index]);
        }

        return Task.FromResult(SkillActionStepResult.Succeeded(
            SkillActionTypes.Hotkey,
            $"Sent hotkey {string.Join("+", keys)}."));
    }

    public Task<SkillActionStepResult> FocusWindowAsync(
        string target,
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Task.FromResult(Unsupported(SkillActionTypes.FocusWindow));
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            return Task.FromResult(SkillActionStepResult.Failed(
                SkillActionTypes.FocusWindow,
                "Focus window action requires a target.",
                "missing_target"));
        }

        var match = IntPtr.Zero;
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            var title = GetWindowTitle(handle);
            if (title.Contains(target, StringComparison.OrdinalIgnoreCase))
            {
                match = handle;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        if (match == IntPtr.Zero)
        {
            return Task.FromResult(SkillActionStepResult.Failed(
                SkillActionTypes.FocusWindow,
                $"No visible window matched '{target}'.",
                "window_not_found"));
        }

        return Task.FromResult(SetForegroundWindow(match)
            ? SkillActionStepResult.Succeeded(SkillActionTypes.FocusWindow, $"Focused window '{target}'.")
            : SkillActionStepResult.Failed(SkillActionTypes.FocusWindow, $"Failed to focus window '{target}'.", "focus_failed"));
    }

    private static SkillActionStepResult Unsupported(string actionType) =>
        SkillActionStepResult.Failed(
            actionType,
            $"{actionType} is currently supported on Windows only.",
            "platform_unsupported");

    private static byte? ResolveVirtualKey(string key)
    {
        var normalized = key.Trim().ToLowerInvariant();
        return normalized switch
        {
            "ctrl" or "control" => 0x11,
            "alt" => 0x12,
            "shift" => 0x10,
            "win" or "windows" or "meta" => 0x5B,
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "escape" or "esc" => 0x1B,
            "space" => 0x20,
            "backspace" => 0x08,
            "delete" or "del" => 0x2E,
            "up" or "arrowup" => 0x26,
            "down" or "arrowdown" => 0x28,
            "left" or "arrowleft" => 0x25,
            "right" or "arrowright" => 0x27,
            _ when normalized.Length == 1 => (byte)char.ToUpperInvariant(normalized[0]),
            _ when normalized.Length > 1
                && normalized[0] == 'f'
                && int.TryParse(normalized[1..], out var functionKey)
                && functionKey is >= 1 and <= 24 => (byte)(0x70 + functionKey - 1),
            _ => null
        };
    }

    private static string GetWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static void KeyDown(byte virtualKey) => KeybdEvent(virtualKey, 0, 0, UIntPtr.Zero);

    private static void KeyUp(byte virtualKey) => KeybdEvent(virtualKey, 0, KeyEventKeyUp, UIntPtr.Zero);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", EntryPoint = "mouse_event")]
    private static extern void MouseEvent(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", EntryPoint = "keybd_event")]
    private static extern void KeybdEvent(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
