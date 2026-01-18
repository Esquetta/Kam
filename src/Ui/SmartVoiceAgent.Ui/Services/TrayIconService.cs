using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using System;
using System.IO;

namespace SmartVoiceAgent.Ui.Services
{
    public class TrayIconService
    {
        private TrayIcon? _trayIcon;
        private NativeMenu? _menu;
        private NativeMenuItem? _statusMenuItem;

        public void Initialize()
        {
            if (Application.Current?.ApplicationLifetime
                is not IClassicDesktopStyleApplicationLifetime)
                return;

            try
            {
                _trayIcon = new TrayIcon
                {
                    // DOĞRU: AssetLoader kullanarak yükle
                    Icon = LoadIconFromAssets(),
                    ToolTipText = "KAM NEURAL // CORE v3.5",
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
            {
                // Yöntem 1: AssetLoader ile (ÖNERİLEN)
                var assetUri = new Uri("avares://SmartVoiceAgent.Ui/Assets/favicon.ico");
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

            var showWindowItem = new NativeMenuItem
            {
                Header = "🖥️ Show Window"
            };
            showWindowItem.Click += OnShowWindowClick;
            _menu.Add(showWindowItem);

            _menu.Add(new NativeMenuItemSeparator());

            _statusMenuItem = new NativeMenuItem
            {
                Header = "⚡ Status: Running",
                IsEnabled = false
            };
            _menu.Add(_statusMenuItem);

            _menu.Add(new NativeMenuItemSeparator());

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
            ShowMainWindow();
        }

        private void OnShowWindowClick(object? sender, EventArgs e)
        {
            ShowMainWindow();
        }

        private void OnExitClick(object? sender, EventArgs e)
        {
            if (Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }

        private void ShowMainWindow()
        {
            if (Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop)
            {
                if (desktop.MainWindow != null)
                {
                    desktop.MainWindow.Show();
                    desktop.MainWindow.WindowState = WindowState.Normal;
                    desktop.MainWindow.Activate();
                }
            }
        }

        public void UpdateToolTip(string text)
        {
            if (_trayIcon != null)
            {
                _trayIcon.ToolTipText = text;
            }
        }

        public void UpdateStatus(string status)
        {
            if (_statusMenuItem != null)
            {
                _statusMenuItem.Header = $"⚡ Status: {status}";
            }
        }

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