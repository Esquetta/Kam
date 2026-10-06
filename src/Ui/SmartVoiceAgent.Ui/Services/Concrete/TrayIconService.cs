using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
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
        private NativeMenuItem? _statusMenuItem;
        private NativeMenuItem? _voiceToggleItem;

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
                    ToolTipText = "Kam - AI Workstation Assistant",
                    IsVisible = true
                };

                CreateContextMenu();
                _trayIcon.Clicked += OnTrayIconClicked;

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

            // Native menus render with the OS theme, so keep labels short and plain (no emoji)
            _menu = new NativeMenu();

            _menu.Add(CreateItem("Open Kam", (_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty)));
            _menu.Add(CreateItem("New task", (_, _) =>
            {
                NewTaskRequested?.Invoke(this, EventArgs.Empty);
                ShowWindowRequested?.Invoke(this, EventArgs.Empty);
            }));

            _voiceToggleItem = new NativeMenuItem
            {
                Header = "Voice listening",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _isVoiceEnabled
            };
            _voiceToggleItem.Click += (_, _) => ToggleVoiceRequested?.Invoke(this, EventArgs.Empty);
            _menu.Add(_voiceToggleItem);

            _menu.Add(new NativeMenuItemSeparator());

            _menu.Add(CreateItem("Skills", (_, _) => NavigateAndShow(NavView.Plugins)));
            _menu.Add(CreateItem("Diagnostics", (_, _) => NavigateAndShow(NavView.Diagnostics)));
            _menu.Add(CreateItem("Integrations", (_, _) => NavigateAndShow(NavView.Integrations)));
            _menu.Add(CreateItem("Settings", (_, _) => NavigateAndShow(NavView.Settings)));

            _menu.Add(new NativeMenuItemSeparator());

            _statusMenuItem = new NativeMenuItem
            {
                Header = "Voice: Ready",
                IsEnabled = false
            };
            _menu.Add(_statusMenuItem);

            _menu.Add(new NativeMenuItemSeparator());

            _menu.Add(CreateItem("About Kam", (_, _) =>
            {
                AboutRequested?.Invoke(this, EventArgs.Empty);
                ShowWindowRequested?.Invoke(this, EventArgs.Empty);
            }));
            _menu.Add(CreateItem("Quit Kam", (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));

            _trayIcon.Menu = _menu;
        }

        private static NativeMenuItem CreateItem(string header, EventHandler onClick)
        {
            var item = new NativeMenuItem { Header = header };
            item.Click += onClick;
            return item;
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
        /// Updates the tooltip text
        /// </summary>
        public void UpdateToolTip(string text)
        {
            if (_trayIcon != null)
            {
                _trayIcon.ToolTipText = text;
            }
        }

        /// <summary>
        /// Updates the status menu item
        /// </summary>
        public void UpdateStatus(string status, bool isRunning = true)
        {
            if (_statusMenuItem != null)
            {
                _statusMenuItem.Header = $"Voice: {status}";
            }
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
            if (_trayIcon != null)
            {
                _trayIcon.Clicked -= OnTrayIconClicked;
                _trayIcon.IsVisible = false;
                _trayIcon = null;
            }
        }
    }
}
