using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Interfaces;
using System.Diagnostics;

namespace SmartVoiceAgent.Application.Handlers.CommandHandlers;

public sealed class IsApplicationRunningCommandHandler : ICommandHandler<IsApplicationRunningCommand, AgentApplicationResponse>
{
    private readonly IApplicationScannerServiceFactory _scannerFactory;

    public IsApplicationRunningCommandHandler(IApplicationScannerServiceFactory scannerFactory)
    {
        _scannerFactory = scannerFactory;
    }

    public async Task<AgentApplicationResponse> Handle(IsApplicationRunningCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // First check if the application is installed
            var scanner = _scannerFactory.Create();
            var applicationInfo = await scanner.FindApplicationAsync(request.ApplicationName);

            if (!applicationInfo.IsInstalled)
            {
                return new AgentApplicationResponse(
                    Success: false,
                    Message: $"{request.ApplicationName} uygulaması yüklü değil.",
                    ApplicationName: request.ApplicationName,
                    IsInstalled: false,
                    IsRunning: false
                );
            }

            // Check if the application is currently running
            var isRunning = IsProcessRunning(request.ApplicationName);

            return new AgentApplicationResponse(
                Success: true,
                Message: isRunning
                    ? $"{request.ApplicationName} uygulaması şu anda çalışıyor."
                    : $"{request.ApplicationName} uygulaması çalışmıyor.",
                ApplicationName: request.ApplicationName,
                ExecutablePath: applicationInfo.ExecutablePath,
                IsInstalled: true,
                IsRunning: isRunning
            );
        }
        catch (Exception ex)
        {
            return new AgentApplicationResponse(
                Success: false,
                Message: $"Uygulama çalışma durumu kontrol edilirken hata oluştu: {ex.Message}",
                ApplicationName: request.ApplicationName
            );
        }
    }

    private static bool IsProcessRunning(string applicationName)
    {
        try
        {
            // Performance: Use Process.GetProcessesByName when possible to reduce allocations
            // First try exact match with GetProcessesByName
            var processesByName = Process.GetProcessesByName(applicationName);
            if (processesByName.Length > 0)
            {
                foreach (var proc in processesByName)
                {
                    proc.Dispose();
                }
                return true;
            }

            // Performance: Get all processes once and use OrdinalIgnoreCase comparison
            // instead of ToLower() which allocates new strings
            var processes = Process.GetProcesses();
            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        // Performance: Use StringComparison.OrdinalIgnoreCase instead of ToLower()
                        if (process.ProcessName.Contains(applicationName, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        if (!string.IsNullOrEmpty(process.MainWindowTitle) &&
                            process.MainWindowTitle.Contains(applicationName, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Some processes may throw exceptions when accessing properties
                        continue;
                    }
                }
            }
            finally
            {
                // Dispose all process handles
                foreach (var process in processes)
                {
                    try { process.Dispose(); } catch { }
                }
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
