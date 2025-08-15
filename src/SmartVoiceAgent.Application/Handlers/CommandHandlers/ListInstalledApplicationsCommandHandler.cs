
using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Interfaces;
using System.Text.Json;

namespace SmartVoiceAgent.Application.Handlers.CommandHandlers;

public sealed class ListInstalledApplicationsCommandHandler : IRequestHandler<ListInstalledApplicationsCommand, CommandResult>
{
    private readonly IApplicationScannerServiceFactory _scannerFactory;

    public ListInstalledApplicationsCommandHandler(IApplicationScannerServiceFactory scannerFactory)
    {
        _scannerFactory = scannerFactory;
    }

    public async Task<CommandResult> Handle(ListInstalledApplicationsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var scanner = _scannerFactory.Create();
            var installedApps = await scanner.GetInstalledApplicationsAsync();

            if (installedApps?.Any() == true)
            {
                var filteredApps = request.IncludeSystemApps
                    ? installedApps
                    : installedApps.Where(app => !IsSystemApplication(app.Name)).ToList();

                var appNames = filteredApps.Select(app => new
                {
                    Name = app.Name,
                    Path = app.Path,
                    IsRunning = app.IsRunning
                }).ToList();

                var message = $"{filteredApps.Count()} uygulama bulundu.";

                return new CommandResult
                {
                    Success = true,
                    Message = message,
                    Data = JsonSerializer.Serialize(appNames, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true
                    })
                };
            }

            return new CommandResult
            {
                Success = true,
                Message = "Hiç yüklü uygulama bulunamadı.",
                Data = "[]"
            };
        }
        catch (Exception ex)
        {
            return new CommandResult
            {
                Success = false,
                Message = $"Yüklü uygulamalar listelenirken hata oluştu: {ex.Message}",
                Error = ex.Message
            };
        }
    }

    private static bool IsSystemApplication(string appName)
    {
        var systemApps = new[]
        {
            "Microsoft", "Windows", "System", "Runtime", "Framework",
            "Redistributable", "Update", "Security", "Driver", "Service"
        };

        return systemApps.Any(systemApp =>
            appName.Contains(systemApp, StringComparison.OrdinalIgnoreCase));
    }
}
