using Avalonia.Controls;
using Avalonia.Threading;
using SmartVoiceAgent.Ui.Services.Concrete;
using SmartVoiceAgent.Ui.Views;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// Shows a corner card and a tray tooltip when a tool call waits for approval and its chat is
/// not on screen: the window is hidden, minimized, in the background, or shows another page or thread.
/// </summary>
public sealed class ApprovalToastNotifier : IApprovalNotifier
{
    private readonly Window _mainWindow;
    private readonly TrayIconService? _trayIconService;
    private readonly List<ApprovalNotice> _pending = [];
    private ApprovalToastWindow? _toast;

    /// <summary>
    /// Creates the notifier.
    /// </summary>
    /// <param name="mainWindow">The window the Review button brings back.</param>
    /// <param name="trayIconService">The tray icon whose tooltip says an approval waits.</param>
    public ApprovalToastNotifier(Window mainWindow, TrayIconService? trayIconService)
    {
        _mainWindow = mainWindow;
        _trayIconService = trayIconService;
    }

    /// <inheritdoc />
    public void Show(ApprovalNotice notice)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Show(notice));
            return;
        }

        if (IsInFrontOfUser(notice))
        {
            return;
        }

        _pending.RemoveAll(pending => pending.RequestId == notice.RequestId);
        _pending.Add(notice);
        _trayIconService?.SetApprovalWaiting(true);
        Refresh();
    }

    /// <inheritdoc />
    public void Dismiss(string requestId)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Dismiss(requestId));
            return;
        }

        if (_pending.RemoveAll(pending => pending.RequestId == requestId) > 0)
        {
            Refresh();
        }
    }

    private bool IsInFrontOfUser(ApprovalNotice notice)
    {
        return notice.IsThreadOnScreen
            && _mainWindow.IsVisible
            && _mainWindow.IsActive
            && _mainWindow.WindowState != WindowState.Minimized;
    }

    private void Refresh()
    {
        if (_pending.Count == 0)
        {
            _trayIconService?.SetApprovalWaiting(false);
            CloseToast();
            return;
        }

        var latest = _pending[^1];
        if (_toast is null)
        {
            _toast = new ApprovalToastWindow();
            _toast.ReviewRequested += (_, _) => Review();
            _toast.DismissRequested += (_, _) => CloseToast();
            _toast.Closed += (_, _) => _toast = null;
        }

        _toast.SetNotice(latest.ToolName, latest.Detail, latest.ThreadTitle, _pending.Count - 1);
        if (!_toast.IsVisible)
        {
            _toast.Show();
        }
    }

    private void Review()
    {
        var latest = _pending.LastOrDefault();
        CloseToast();

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
        latest?.Open();
    }

    private void CloseToast()
    {
        var toast = _toast;
        _toast = null;
        toast?.Close();
    }
}
