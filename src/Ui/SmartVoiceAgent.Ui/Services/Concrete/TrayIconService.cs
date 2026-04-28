using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
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
        private NativeMenuItem? _showWindowItem;

        /// <summary>
        /// Event raised when user requests to show the main window
        /// </summary>
        public event EventHandler? ShowWindowRequested;

        /// <summary>
        /// Event raised when user requests to open settings
        /// </summary>
        public event EventHandler? OpenSettingsRequested;

        /// <summary>
        /// Event raised when user requests to toggle voice recognition
        /// </summary>
        public event EventHandler? ToggleVoiceRequested;

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

            _menu = new NativeMenu();

            // Show Window
            _showWindowItem = new NativeMenuItem
            {
                Header = "🖥️ Show Window"
            };
            _showWindowItem.Click += OnShowWindowClick;
            _menu.Add(_showWindowItem);

            _menu.Add(new NativeMenuItemSeparator());

            // Quick Actions Section
            var quickActionsHeader = new NativeMenuItem
            {
                Header = "⚡ Quick Actions",
                IsEnabled = false
            };
            _menu.Add(quickActionsHeader);

            // Voice Toggle
            _voiceToggleItem = new NativeMenuItem
            {
                Header = "🎤 Enable Voice"
            };
            _voiceToggleItem.Click += OnToggleVoiceClick;
            _menu.Add(_voiceToggleItem);

            // Settings
            var settingsItem = new NativeMenuItem
            {
                Header = "⚙️ Settings"
            };
            settingsItem.Click += OnSettingsClick;
            _menu.Add(settingsItem);

            _menu.Add(new NativeMenuItemSeparator());

            // Status Section
            _statusMenuItem = new NativeMenuItem
            {
                Header = "● Status: Ready",
                IsEnabled = false
            };
            _menu.Add(_statusMenuItem);

            _menu.Add(new NativeMenuItemSeparator());

            // About
            var aboutItem = new NativeMenuItem
            {
                Header = "ℹ️ About Kam"
            };
            aboutItem.Click += OnAboutClick;
            _menu.Add(aboutItem);

            // Exit
            var exitItem = new NativeMenuItem
            {
                Header = "❌ Exit"
            };
            exitItem.Click += OnExitClick;
            _menu.Add(exitItem);

            _trayIcon.Menu = _menu;
        }

        private void OnTrayIconClicked(object? sender, EventArgs e)
        {
            ShowWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnShowWindowClick(object? sender, EventArgs e)
        {
            ShowWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnSettingsClick(object? sender, EventArgs e)
        {
            OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
            // Also show window when opening settings
            ShowWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnToggleVoiceClick(object? sender, EventArgs e)
        {
            ToggleVoiceRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnAboutClick(object? sender, EventArgs e)
        {
            AboutRequested?.Invoke(this, EventArgs.Empty);
            ShowWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnExitClick(object? sender, EventArgs e)
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Updates the voice toggle menu item based on current state
        /// </summary>
        public void SetVoiceEnabled(bool enabled)
        {
            _isVoiceEnabled = enabled;
            if (_voiceToggleItem != null)
            {
                _voiceToggleItem.Header = enabled ? "🔴 Disable Voice" : "🎤 Enable Voice";
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
                var indicator = isRunning ? "●" : "○";
                _statusMenuItem.Header = $"{indicator} Status: {status}";
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
