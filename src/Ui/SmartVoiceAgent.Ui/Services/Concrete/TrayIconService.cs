using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using SmartVoiceAgent.Ui.ViewModels;
using System;
using System.IO;

namespace SmartVoiceAgent.Ui.Services.Concrete
{
    /// <summary>
    /// Service for managing the system tray icon and context menu
    /// </summary>
    public class TrayIconService
    {
        private TrayIcon? _trayIcon;
        private NativeMenu? _menu;
        private NativeMenuItem? _openItem;
        private NativeMenuItem? _newTaskItem;
        private NativeMenuItem? _voiceToggleItem;
        private NativeMenuItem? _extensionsItem;
        private NativeMenuItem? _skillsItem;
        private NativeMenuItem? _diagnosticsItem;
        private NativeMenuItem? _integrationsItem;
        private NativeMenuItem? _settingsItem;
        private NativeMenuItem? _statusMenuItem;
        private NativeMenuItem? _aboutItem;
        private NativeMenuItem? _quitItem;

        /// <summary>
        /// Event raised when user requests to show the main window
        /// </summary>
        public event EventHandler? ShowWindowRequested;

        /// <summary>
        /// Event raised when user picks a page from the tray menu
        /// </summary>
        public event EventHandler<NavView>? NavigateRequested;

        /// <summary>
        /// Event raised when user requests to toggle voice recognition
        /// </summary>
        public event EventHandler? ToggleVoiceRequested;

        /// <summary>
        /// Raised when "New task" is clicked: open a new agent chat and show the window.
        /// </summary>
        public event EventHandler? NewTaskRequested;

        /// <summary>
        /// Event raised when user requests to show about dialog
        /// </summary>
        public event EventHandler? AboutRequested;

        /// <summary>
        /// Event raised when user requests to exit the application
        /// </summary>
        public event EventHandler? ExitRequested;

        private bool _isVoiceEnabled = false;
        private bool _isVoiceListening = false;
        private TrayToolTip _toolTip = TrayToolTip.Idle;
        private string _customToolTip = string.Empty;

        private enum TrayToolTip
        {
            Idle,
            WaitingForApproval,
            Custom
        }

        public void Initialize()
        {
            if (global::Avalonia.Application.Current?.ApplicationLifetime
                is not IClassicDesktopStyleApplicationLifetime)
                return;

            try
            {
                _trayIcon = new TrayIcon
                {
                    Icon = LoadIconFromAssets(),
                    ToolTipText = CurrentToolTipText(),
                    IsVisible = true
                };

                CreateContextMenu();
                _trayIcon.Clicked += OnTrayIconClicked;
                LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

                Console.WriteLine("✓ Tray icon initialized successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Tray icon initialization error: {ex.Message}");
            }
        }

        private WindowIcon LoadIconFromAssets()
        {
            try
            {                var assetUri = new Uri("avares://SmartVoiceAgent.Ui/Assets/favicon.ico");
                var stream = AssetLoader.Open(assetUri);
                var icon = new WindowIcon(stream);

                Console.WriteLine("✓ Icon loaded from Assets using AssetLoader");
                return icon;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ AssetLoader failed: {ex.Message}");

                try
                {
                    var exePath = AppDomain.CurrentDomain.BaseDirectory;
                    var iconPath = Path.Combine(exePath, "Assets", "favicon.ico");

                    if (File.Exists(iconPath))
                    {
                        var icon = new WindowIcon(iconPath);
                        Console.WriteLine($"✓ Icon loaded from file system: {iconPath}");
                        return icon;
                    }
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"✗ File system load failed: {ex2.Message}");
                }

                throw new FileNotFoundException("Icon file not found in Assets");
            }
        }

        private void CreateContextMenu()
        {
            if (_trayIcon == null)
                return;

            // Native menus render with the OS theme, so keep labels short and plain (no emoji).
            // Labels are set in ApplyText, which runs again when the interface language changes.
            _menu = new NativeMenu();

            _openItem = CreateItem((_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty));
            _menu.Add(_openItem);
            _newTaskItem = CreateItem((_, _) =>
            {
                NewTaskRequested?.Invoke(this, EventArgs.Empty);
                ShowWindowRequested?.Invoke(this, EventArgs.Empty);
            });
            _menu.Add(_newTaskItem);

            _voiceToggleItem = new NativeMenuItem
            {
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _isVoiceEnabled
            };
            _voiceToggleItem.Click += (_, _) => ToggleVoiceRequested?.Invoke(this, EventArgs.Empty);
            _menu.Add(_voiceToggleItem);

            _menu.Add(new NativeMenuItemSeparator());

            _extensionsItem = CreateItem((_, _) => NavigateAndShow(NavView.Extensions));
            _menu.Add(_extensionsItem);
            _skillsItem = CreateItem((_, _) => NavigateAndShow(NavView.Plugins));
            _menu.Add(_skillsItem);
            _diagnosticsItem = CreateItem((_, _) => NavigateAndShow(NavView.Diagnostics));
            _menu.Add(_diagnosticsItem);
            _integrationsItem = CreateItem((_, _) => NavigateAndShow(NavView.Integrations));
            _menu.Add(_integrationsItem);
            _settingsItem = CreateItem((_, _) => NavigateAndShow(NavView.Settings));
            _menu.Add(_settingsItem);

            _menu.Add(new NativeMenuItemSeparator());

            _statusMenuItem = new NativeMenuItem
            {
                IsEnabled = false
            };
            _menu.Add(_statusMenuItem);

            _menu.Add(new NativeMenuItemSeparator());

            _aboutItem = CreateItem((_, _) =>
            {
                AboutRequested?.Invoke(this, EventArgs.Empty);
                ShowWindowRequested?.Invoke(this, EventArgs.Empty);
            });
            _menu.Add(_aboutItem);
            _quitItem = CreateItem((_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
            _menu.Add(_quitItem);

            ApplyText();
            _trayIcon.Menu = _menu;
        }

        private static NativeMenuItem CreateItem(EventHandler onClick)
        {
            var item = new NativeMenuItem();
            item.Click += onClick;
            return item;
        }

        /// <summary>
        /// Writes the menu labels, the voice line and the tooltip in the current interface language.
        /// </summary>
        private void ApplyText()
        {
            SetHeader(_openItem, Loc.Get("Shell.Tray.Open"));
            SetHeader(_newTaskItem, Loc.Get("Shell.Tray.NewTask"));
            SetHeader(_voiceToggleItem, Loc.Get("Shell.Tray.VoiceListening"));
            SetHeader(_extensionsItem, Loc.Get("Shell.Nav.Extensions"));
            SetHeader(_skillsItem, Loc.Get("Shell.Nav.Skills"));
            SetHeader(_diagnosticsItem, Loc.Get("Shell.Nav.Diagnostics"));
            SetHeader(_integrationsItem, Loc.Get("Shell.Nav.Integrations"));
            SetHeader(_settingsItem, Loc.Get("Shell.Nav.Settings"));
            SetHeader(_aboutItem, Loc.Get("Shell.Tray.About"));
            SetHeader(_quitItem, Loc.Get("Shell.Tray.Quit"));

            ApplyVoiceStatus();
            ApplyToolTip();
        }

        private void ApplyVoiceStatus()
        {
            SetHeader(_statusMenuItem, _isVoiceListening
                ? Loc.Get("Shell.Tray.VoiceStatusListening")
                : Loc.Get("Shell.Tray.VoiceStatusReady"));
        }

        private static void SetHeader(NativeMenuItem? item, string header)
        {
            if (item != null)
            {
                item.Header = header;
            }
        }

        private void ApplyToolTip()
        {
            if (_trayIcon != null)
            {
                _trayIcon.ToolTipText = CurrentToolTipText();
            }
        }

        private string CurrentToolTipText()
        {
            return _toolTip switch
            {
                TrayToolTip.WaitingForApproval => Loc.Get("Shell.Tray.ToolTipWaiting"),
                TrayToolTip.Custom => _customToolTip,
                _ => Loc.Get("Shell.Tray.ToolTip")
            };
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                ApplyText();
            }
            else
            {
                Dispatcher.UIThread.Post(ApplyText);
            }
        }

        private void NavigateAndShow(NavView view)
        {
            NavigateRequested?.Invoke(this, view);
            ShowWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnTrayIconClicked(object? sender, EventArgs e)
        {
            ShowWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Updates the voice toggle menu item based on current state
        /// </summary>
        public void SetVoiceEnabled(bool enabled)
        {
            _isVoiceEnabled = enabled;
            if (_voiceToggleItem != null)
            {
                _voiceToggleItem.IsChecked = enabled;
            }
        }

        /// <summary>
        /// Shows <paramref name="text"/> as the tooltip as given; it stays as is when the language changes.
        /// </summary>
        /// <param name="text">The tooltip text.</param>
        public void UpdateToolTip(string text)
        {
            _toolTip = TrayToolTip.Custom;
            _customToolTip = text;
            ApplyToolTip();
        }

        /// <summary>
        /// Says in the tooltip that a tool call waits for approval, or goes back to the usual tooltip.
        /// Both follow the interface language.
        /// </summary>
        /// <param name="waiting">Whether an approval waits.</param>
        public void SetApprovalWaiting(bool waiting)
        {
            _toolTip = waiting ? TrayToolTip.WaitingForApproval : TrayToolTip.Idle;
            ApplyToolTip();
        }

        /// <summary>
        /// Updates the voice line of the menu: "Voice: Listening" while <paramref name="isRunning"/> is true,
        /// otherwise "Voice: Ready". The line is built from <paramref name="isRunning"/> in the interface language.
        /// </summary>
        /// <param name="status">The status word the caller shows elsewhere ("Ready" or "Listening").</param>
        /// <param name="isRunning">Whether voice recognition is listening.</param>
        public void UpdateStatus(string status, bool isRunning = true)
        {
            _isVoiceListening = isRunning;
            ApplyVoiceStatus();
        }

        /// <summary>
        /// Updates the tray icon
        /// </summary>
        public void UpdateIcon(string iconPath)
        {
            if (_trayIcon != null)
            {
                try
                {
                    var assetUri = new Uri($"avares://SmartVoiceAgent.Ui/{iconPath}");
                    var stream = AssetLoader.Open(assetUri);
                    _trayIcon.Icon = new WindowIcon(stream);

                    Console.WriteLine($"✓ Icon updated: {iconPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ Failed to update icon: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Shows or hides the tray icon
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = visible;
            }
        }

        public void Dispose()
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            if (_trayIcon != null)
            {
                _trayIcon.Clicked -= OnTrayIconClicked;
                _trayIcon.IsVisible = false;
                _trayIcon = null;
            }
        }
    }
}
