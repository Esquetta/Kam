using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// Service for handling application errors, showing dialogs, and logging to file.
/// </summary>
public class ErrorHandlingService
{
    private readonly ILogger _logger;
    private readonly string _logDirectory;
    private Window? _mainWindow;

    public ErrorHandlingService()
    {
        _logDirectory = GetLogDirectory();
        Directory.CreateDirectory(_logDirectory);

        // Configure Serilog for file logging
        var logPath = Path.Combine(_logDirectory, "kam-.log");
        
        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        _logger.Information("ErrorHandlingService initialized. Log directory: {LogDirectory}", _logDirectory);
    }

    /// <summary>
    /// Sets the main window reference for dialog parent
    /// </summary>
    public void SetMainWindow(Window mainWindow)
    {
        _mainWindow = mainWindow;
    }

    /// <summary>
    /// Gets the log directory path in AppData/Local
    /// </summary>
    private static string GetLogDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "Kam", "Logs");
    }

    /// <summary>
    /// Gets the current log file path
    /// </summary>
    public string GetCurrentLogFilePath()
    {
        var date = DateTime.Now.ToString("yyyyMMdd");
        return Path.Combine(_logDirectory, $"kam-{date}.log");
    }

    /// <summary>
    /// Logs a message to the log file
    /// </summary>
    public void LogInformation(string message)
    {
        _logger.Information(message);
    }

    /// <summary>
    /// Logs a warning to the log file
    /// </summary>
    public void LogWarning(string message)
    {
        _logger.Warning(message);
    }

    /// <summary>
    /// Logs an error to the log file
    /// </summary>
    public void LogError(Exception ex, string? message = null)
    {
        if (message != null)
        {
            _logger.Error(ex, message);
        }
        else
        {
            _logger.Error(ex, ex.Message);
        }
    }

    /// <summary>
    /// Handles a fatal unhandled exception - logs and shows error dialog
    /// </summary>
    public async Task HandleFatalExceptionAsync(Exception ex)
    {
        // Log the error
        _logger.Fatal(ex, "Fatal unhandled exception");

        // Show error dialog on UI thread
        await ShowErrorDialogAsync(
            "Critical Error",
            "An unexpected error occurred and the application may need to close.",
            ex);
    }

    /// <summary>
    /// Handles an unobserved task exception - logs but doesn't crash
    /// </summary>
    public void HandleUnobservedException(Exception ex)
    {
        _logger.Error(ex, "Unobserved task exception");
    }

    /// <summary>
    /// Shows an error dialog with exception details
    /// </summary>
    public async Task ShowErrorDialogAsync(string title, string message, Exception? ex = null)
    {
        try
        {
            var dialog = new Window
            {
                Title = title,
                Width = 500,
                Height = ex != null ? 400 : 200,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Icon = _mainWindow?.Icon
            };

            var stackPanel = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 15
            };

            // Error icon and title
            var titleBlock = new TextBlock
            {
                Text = "❌ " + title,
                FontSize = 18,
                FontWeight = Avalonia.Media.FontWeight.Bold,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };
            stackPanel.Children.Add(titleBlock);

            // Message
            var messageBlock = new TextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };
            stackPanel.Children.Add(messageBlock);

            // Exception details (expandable)
            if (ex != null)
            {
                var detailsHeader = new TextBlock
                {
                    Text = "Error Details:",
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    Margin = new Avalonia.Thickness(0, 10, 0, 0)
                };
                stackPanel.Children.Add(detailsHeader);

                var scrollViewer = new ScrollViewer
                {
                    Height = 150,
                    Content = new TextBlock
                    {
                        Text = $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
                        FontFamily = new Avalonia.Media.FontFamily("Consolas, Courier New, monospace"),
                        FontSize = 11,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    }
                };
                stackPanel.Children.Add(scrollViewer);

                // Log file location
                var logInfo = new TextBlock
                {
                    Text = $"Details logged to: {GetCurrentLogFilePath()}",
                    FontSize = 11,
                    Foreground = Avalonia.Media.Brushes.Gray,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                };
                stackPanel.Children.Add(logInfo);
            }

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Avalonia.Thickness(0, 10, 0, 0)
            };

            // Copy to clipboard button
            if (ex != null)
            {
                var copyButton = new Button
                {
                    Content = "Copy Error"
                };
                copyButton.Click += async (s, e) =>
                {
                    var errorText = $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}";
                    if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        // Use clipboard API
                        var clipboard = desktop.MainWindow?.Clipboard;
                        if (clipboard != null)
                        {
                            await clipboard.SetTextAsync(errorText);
                        }
                    }
                };
                buttonPanel.Children.Add(copyButton);
            }

            // Open logs button
            var openLogsButton = new Button
            {
                Content = "Open Logs"
            };
            openLogsButton.Click += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _logDirectory,
                        UseShellExecute = true
                    });
                }
                catch { }
            };
            buttonPanel.Children.Add(openLogsButton);

            // OK button
            var okButton = new Button
            {
                Content = "OK",
                IsDefault = true,
                Width = 80
            };
            okButton.Click += (s, e) => dialog.Close();
            buttonPanel.Children.Add(okButton);

            stackPanel.Children.Add(buttonPanel);

            dialog.Content = new Border
            {
                Child = stackPanel,
                Padding = new Avalonia.Thickness(10)
            };

            // Show dialog
            if (_mainWindow != null)
            {
                await dialog.ShowDialog(_mainWindow);
            }
            else
            {
                dialog.Show();
                // Wait for dialog to close
                var tcs = new TaskCompletionSource<object?>();
                dialog.Closed += (s, e) => tcs.SetResult(null);
                await tcs.Task;
            }
        }
        catch (Exception dialogEx)
        {
            // If dialog fails, at least log it
            _logger.Error(dialogEx, "Failed to show error dialog");
            
            // Fallback to console
            Console.WriteLine($"ERROR: {title}");
            Console.WriteLine(message);
            if (ex != null)
            {
                Console.WriteLine($"Exception: {ex}");
            }
        }
    }

    /// <summary>
    /// Shows a simple information dialog
    /// </summary>
    public async Task ShowInfoDialogAsync(string title, string message)
    {
        try
        {
            var dialog = new Window
            {
                Title = title,
                Width = 400,
                Height = 150,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Icon = _mainWindow?.Icon
            };

            var stackPanel = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 15
            };

            var messageBlock = new TextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };
            stackPanel.Children.Add(messageBlock);

            var okButton = new Button
            {
                Content = "OK",
                IsDefault = true,
                Width = 80,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
            };
            okButton.Click += (s, e) => dialog.Close();
            stackPanel.Children.Add(okButton);

            dialog.Content = stackPanel;

            if (_mainWindow != null)
            {
                await dialog.ShowDialog(_mainWindow);
            }
            else
            {
                dialog.Show();
                var tcs = new TaskCompletionSource<object?>();
                dialog.Closed += (s, e) => tcs.SetResult(null);
                await tcs.Task;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to show info dialog");
            Console.WriteLine($"{title}: {message}");
        }
    }
}
