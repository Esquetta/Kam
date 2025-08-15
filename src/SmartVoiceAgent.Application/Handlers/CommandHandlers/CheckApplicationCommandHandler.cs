using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Interfaces;
using System.Diagnostics;

namespace SmartVoiceAgent.Application.Handlers.CommandHandlers;

public sealed class CheckApplicationCommandHandler : IRequestHandler<CheckApplicationCommand, AgentApplicationResponse>
{
    private readonly IApplicationScannerServiceFactory _scannerFactory;

    public CheckApplicationCommandHandler(IApplicationScannerServiceFactory scannerFactory)
    {
        _scannerFactory = scannerFactory;
    }

    public async Task<AgentApplicationResponse> Handle(CheckApplicationCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var scanner = _scannerFactory.Create();
            var applicationInfo = await scanner.FindApplicationAsync(request.ApplicationName);

            if (applicationInfo.IsInstalled)
            {
                // Check if the application is currently running
                var isRunning = IsProcessRunning(request.ApplicationName);

                var message = isRunning
                    ? $"{request.ApplicationName} uygulaması yüklü ve şu anda çalışıyor."
                    : $"{request.ApplicationName} uygulaması yüklü ancak çalışmıyor.";

                return new AgentApplicationResponse(
                    Success: true,
                    Message: message,
                    ApplicationName: request.ApplicationName,
                    ExecutablePath: applicationInfo.ExecutablePath,
                    IsInstalled: true,
                    IsRunning: isRunning
                );
            }

            return new AgentApplicationResponse(
                Success: false,
                Message: $"{request.ApplicationName} uygulaması yüklü değil.",
                ApplicationName: request.ApplicationName,
                IsInstalled: false,
                IsRunning: false
            );
        }
        catch (Exception ex)
        {
            return new AgentApplicationResponse(
                Success: false,
                Message: $"Uygulama kontrol edilirken hata oluştu: {ex.Message}",
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
