using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Interfaces;
using System.Diagnostics;

namespace SmartVoiceAgent.Application.Handlers.CommandHandlers;

public sealed class IsApplicationRunningCommandHandler : IRequestHandler<IsApplicationRunningCommand, AgentApplicationResponse>
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
            var processes = Process.GetProcesses();
            var appNameLower = applicationName.ToLower();

            return processes.Any(process =>
            {
                try
                {
                    return process.ProcessName.ToLower().Contains(appNameLower) ||
                           (!string.IsNullOrEmpty(process.MainWindowTitle) &&
                            process.MainWindowTitle.ToLower().Contains(appNameLower));
                }
                catch
                {
                    // Some processes may throw exceptions when accessing properties
                    return false;
                }
            });
        }
        catch (Exception)
        {
            return false;
        }
    }
}
