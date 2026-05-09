using System.Diagnostics;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Updates;

namespace SmartVoiceAgent.Infrastructure.Services;

public sealed class ApplicationRestartPlanner : IApplicationRestartPlanner
{
    public ApplicationRestartPlan CreateRestartPlan(string? updatePackagePath = null)
    {
        var executablePath = ResolveExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return new ApplicationRestartPlan(
                false,
                "Kam restart plan is unavailable because the current executable path could not be resolved.",
                executablePath,
                updatePackagePath,
                []);
        }

        if (string.IsNullOrWhiteSpace(updatePackagePath))
        {
            return new ApplicationRestartPlan(
                true,
                "Kam can restart the current application process.",
                executablePath,
                null,
                [
                    $"Start \"{executablePath}\"",
                    "Close the current Kam process after the new process starts"
                ]);
        }

        string packagePath;
        try
        {
            packagePath = Path.GetFullPath(updatePackagePath);
        }
        catch (ArgumentException)
        {
            return UnsupportedPackagePlan(
                executablePath,
                updatePackagePath,
                "Update package path is invalid.");
        }
        catch (NotSupportedException)
        {
            return UnsupportedPackagePlan(
                executablePath,
                updatePackagePath,
                "Update package path is not supported.");
        }

        if (!File.Exists(packagePath))
        {
            return UnsupportedPackagePlan(
                executablePath,
                packagePath,
                "Update package could not be found.");
        }

        var extension = Path.GetExtension(packagePath).ToLowerInvariant();
        if (extension == ".zip")
        {
            return new ApplicationRestartPlan(
                false,
                "ZIP update packages require manual extraction before restart.",
                executablePath,
                packagePath,
                [
                    $"Extract \"{packagePath}\"",
                    $"Start \"{executablePath}\""
                ]);
        }

        if (extension is not ".msi" and not ".exe")
        {
            return UnsupportedPackagePlan(
                executablePath,
                packagePath,
                $"Update package type '{extension}' is not supported.");
        }

        var installerStep = extension == ".msi"
            ? $"Start msiexec /i \"{packagePath}\""
            : $"Start \"{packagePath}\"";

        return new ApplicationRestartPlan(
            true,
            "Kam can hand off to the downloaded installer and close the current app.",
            executablePath,
            packagePath,
            [
                installerStep,
                "Close the current Kam process after installer launch",
                "Relaunch Kam from the installer or Start Menu after upgrade"
            ]);
    }

    private static string? ResolveExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return Environment.ProcessPath;
        }

        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (global::System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static ApplicationRestartPlan UnsupportedPackagePlan(
        string executablePath,
        string updatePackagePath,
        string message)
    {
        return new ApplicationRestartPlan(
            false,
            message,
            executablePath,
            updatePackagePath,
            [
                "Download a supported Kam .msi or .exe package before restart handoff",
                $"Start \"{executablePath}\" only after the package is verified"
            ]);
    }
}
