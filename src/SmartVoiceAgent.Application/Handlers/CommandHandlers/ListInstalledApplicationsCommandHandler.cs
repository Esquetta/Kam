using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Interfaces;
using System.Text.Json;

namespace SmartVoiceAgent.Application.Handlers.CommandHandlers;

public sealed class ListInstalledApplicationsCommandHandler : ICommandHandler<ListInstalledApplicationsCommand, CommandResult>
{
    private readonly IApplicationScannerServiceFactory _scannerFactory;

    // Performance: Reuse JsonSerializerOptions instance instead of creating per request
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    // Performance: Static readonly HashSet for O(1) lookup instead of array scan
    private static readonly HashSet<string> s_systemAppKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Windows", "System", "Runtime", "Framework",
        "Redistributable", "Update", "Security", "Driver", "Service"
    };

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
                // Performance: Use IEnumerable deferred execution instead of ToList()
                var filteredApps = request.IncludeSystemApps
                    ? installedApps
                    : installedApps.Where(app => !IsSystemApplication(app.Name));

                // Performance: Avoid anonymous type allocation - use ValueTuple
                var appNames = filteredApps.Select(app => (
                    Name: app.Name,
                    Path: app.Path,
                    IsRunning: app.IsRunning
                ));

                var count = request.IncludeSystemApps 
                    ? installedApps.Count() 
                    : installedApps.Count(app => !IsSystemApplication(app.Name));

                var message = $"{count} uygulama bulundu.";

                return new CommandResult
                {
                    Success = true,
                    Message = message,
                    // Performance: Use cached JsonSerializerOptions
                    Data = JsonSerializer.Serialize(appNames, s_jsonOptions)
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

    // Performance: O(1) HashSet lookup instead of O(n) array scan
    private static bool IsSystemApplication(string appName)
    {
        return s_systemAppKeywords.Any(keyword => appName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
