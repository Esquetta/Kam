using System.Diagnostics;

namespace SmartVoiceAgent.Infrastructure.Security;

/// <summary>
/// Runs POSIX shell commands (bash, zsh) with safely quoted arguments.
/// </summary>
public static class PosixShell
{
    /// <summary>
    /// Quotes a value as a single POSIX shell word so it can never be read as shell syntax.
    /// </summary>
    /// <param name="value">The raw value to embed in a command line.</param>
    /// <returns>The value wrapped in single quotes, with embedded single quotes escaped.</returns>
    public static string Quote(string? value)
    {
        return "'" + (value ?? string.Empty).Replace("'", "'\\''") + "'";
    }

    /// <summary>
    /// Runs a command through the given shell and returns its standard output.
    /// Returns an empty string when the shell cannot be started.
    /// </summary>
    /// <param name="shellPath">Absolute path of the shell, such as /bin/bash.</param>
    /// <param name="command">The command text. Interpolated values must be wrapped with <see cref="Quote"/>.</param>
    public static string Run(string shellPath, string command)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = shellPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // ArgumentList passes the command as one argv entry, so no outer quoting is needed.
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(command);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return string.Empty;
            }

            // Drain stderr concurrently so a chatty command cannot fill the pipe and block.
            var errorTask = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            errorTask.GetAwaiter().GetResult();

            return output;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
