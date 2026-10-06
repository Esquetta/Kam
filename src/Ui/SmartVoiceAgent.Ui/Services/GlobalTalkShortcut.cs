using Avalonia.Controls;
using Avalonia.Input;
using System;
using System.Runtime.InteropServices;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// Registers the talk shortcut with Windows, so it starts and stops talking while Kam is in the background.
/// On other systems, and when another app holds the shortcut, <see cref="IsRegistered"/> stays false and the
/// window handles the shortcut only while it has focus.
/// </summary>
public sealed class GlobalTalkShortcut : IDisposable
{
    private const int HotkeyId = 0x4B41;
    private const uint WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly TopLevel _window;
    private readonly Action _pressed;
    private Win32Properties.CustomWndProcHookCallback? _hook;
    private IntPtr _handle;
    private bool _disposed;

    /// <summary>
    /// Creates the shortcut for a window. Nothing is registered until <see cref="Register"/>.
    /// </summary>
    /// <param name="window">The window whose message loop receives the shortcut.</param>
    /// <param name="pressed">Runs on the UI thread when the shortcut is pressed.</param>
    public GlobalTalkShortcut(TopLevel window, Action pressed)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _pressed = pressed ?? throw new ArgumentNullException(nameof(pressed));
    }

    /// <summary>
    /// Gets whether Windows delivers the shortcut to Kam.
    /// </summary>
    public bool IsRegistered { get; private set; }

    /// <summary>
    /// Replaces the registered shortcut. Returns <c>false</c> when the shortcut is empty or invalid, the
    /// system isn't Windows, or another app already holds it.
    /// </summary>
    /// <param name="shortcut">A key gesture such as <c>Ctrl+Alt+Space</c>.</param>
    public bool Register(string? shortcut)
    {
        Unregister();
        if (_disposed || !OperatingSystem.IsWindows() || !TryParse(shortcut, out var modifiers, out var virtualKey))
        {
            return false;
        }

        _handle = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_handle == IntPtr.Zero)
        {
            return false;
        }

        if (_hook is null)
        {
            _hook = OnMessage;
            Win32Properties.AddWndProcHookCallback(_window, _hook);
        }

        IsRegistered = RegisterHotKey(_handle, HotkeyId, modifiers | ModNoRepeat, virtualKey);
        return IsRegistered;
    }

    /// <summary>
    /// Reads a shortcut into Windows hotkey modifiers and a virtual-key code. Only keys that make sense as a
    /// global shortcut are accepted: letters, digits, F1-F24, Space, Pause, Insert, Home, End, Page Up,
    /// Page Down and Scroll Lock.
    /// </summary>
    /// <param name="shortcut">A key gesture such as <c>Ctrl+Alt+Space</c>.</param>
    /// <param name="modifiers">The <c>MOD_*</c> flags.</param>
    /// <param name="virtualKey">The <c>VK_*</c> code.</param>
    public static bool TryParse(string? shortcut, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (!TryParseGesture(shortcut, out var gesture))
        {
            return false;
        }

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            modifiers |= ModAlt;
        }

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            modifiers |= ModControl;
        }

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            modifiers |= ModShift;
        }

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            modifiers |= ModWin;
        }

        virtualKey = ToVirtualKey(gesture.Key);
        return virtualKey != 0;
    }

    /// <summary>
    /// Parses a shortcut such as <c>Ctrl+Alt+Space</c>; returns <c>false</c> when it is empty or invalid.
    /// </summary>
    /// <param name="shortcut">The saved shortcut.</param>
    /// <param name="gesture">The parsed gesture.</param>
    public static bool TryParseGesture(string? shortcut, out KeyGesture gesture)
    {
        gesture = null!;
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return false;
        }

        try
        {
            gesture = KeyGesture.Parse(shortcut.Trim());
            return gesture.Key != Key.None;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unregister();
        if (_hook is not null && OperatingSystem.IsWindows())
        {
            Win32Properties.RemoveWndProcHookCallback(_window, _hook);
        }

        _hook = null;
    }

    private void Unregister()
    {
        if (IsRegistered && OperatingSystem.IsWindows())
        {
            UnregisterHotKey(_handle, HotkeyId);
        }

        IsRegistered = false;
    }

    private IntPtr OnMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt64() == HotkeyId)
        {
            handled = true;
            _pressed();
        }

        return IntPtr.Zero;
    }

    private static uint ToVirtualKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return (uint)(0x41 + (key - Key.A));
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return (uint)(0x30 + (key - Key.D0));
        }

        if (key is >= Key.F1 and <= Key.F24)
        {
            return (uint)(0x70 + (key - Key.F1));
        }

        return key switch
        {
            Key.Space => 0x20,
            Key.Pause => 0x13,
            Key.Insert => 0x2D,
            Key.Home => 0x24,
            Key.End => 0x23,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.Scroll => 0x91,
            _ => 0
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
