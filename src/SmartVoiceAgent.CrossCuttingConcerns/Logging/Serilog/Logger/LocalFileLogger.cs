using Serilog;

namespace Core.CrossCuttingConcerns.Logging.Serilog.Logger;

/// <summary>
/// Writes pipeline logs to a daily rolling file under %LocalAppData%/Kam/Logs.
/// Used when no MongoDB log database is configured, which is the default for a desktop install.
/// </summary>
public sealed class LocalFileLogger : LoggerServiceBase, IDisposable
{
    private readonly global::Serilog.Core.Logger _fileLogger;

    /// <summary>
    /// Creates a logger that writes to <c>pipeline-.log</c> in the given folder.
    /// </summary>
    /// <param name="directory">Folder for the log files; defaults to %LocalAppData%/Kam/Logs.</param>
    public LocalFileLogger(string? directory = null)
    {
        directory ??= DefaultDirectory();
        LogDirectory = directory;

        _fileLogger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(directory, "pipeline-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 5_000_000,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] {Message}{NewLine}{Exception}")
            .CreateLogger();
        Logger = _fileLogger;
    }

    /// <summary>Gets the folder the log files are written to.</summary>
    public string LogDirectory { get; }

    /// <summary>Gets the default log folder, %LocalAppData%/Kam/Logs.</summary>
    public static string DefaultDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kam",
            "Logs");

    /// <summary>Flushes and closes the log file.</summary>
    public void Dispose() => _fileLogger.Dispose();
}
