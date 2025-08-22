using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers.CommandHandlers;
public sealed class ControlWiFiCommandHandler : IRequestHandler<ControlWiFiCommand, CommandResult>
{
    private readonly ISystemControlServiceFactory _systemControlFactory;

    public ControlWiFiCommandHandler(ISystemControlServiceFactory systemControlFactory)
    {
        _systemControlFactory = systemControlFactory;
    }

    public async Task<CommandResult> Handle(ControlWiFiCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var systemControl = _systemControlFactory.CreateSystemService();
            var action = request.Action.ToLower();
            bool success = false;
            string message = "";

            switch (action)
            {
                case "enable":
                case "aç":
                case "açık":
                case "etkinleştir":
                    success = await systemControl.EnableWiFiAsync();
                    message = success ? "WiFi açıldı." : "WiFi açılamadı.";
                    break;

                case "disable":
                case "kapat":
                case "kapalı":
                case "devre dışı":
                    success = await systemControl.DisableWiFiAsync();
                    message = success ? "WiFi kapatıldı." : "WiFi kapatılamadı.";
                    break;

                case "toggle":
                case "değiştir":
                    var currentStatus = await systemControl.GetWiFiStatusAsync();
                    if (currentStatus)
                    {
                        success = await systemControl.DisableWiFiAsync();
                        message = success ? "WiFi kapatıldı." : "WiFi kapatılamadı.";
                    }
                    else
                    {
                        success = await systemControl.EnableWiFiAsync();
                        message = success ? "WiFi açıldı." : "WiFi açılamadı.";
                    }
                    break;

                case "status":
                case "durum":
                    var status = await systemControl.GetWiFiStatusAsync();
                    message = $"WiFi durumu: {(status ? "Açık" : "Kapalı")}";
                    success = true;
                    break;

                default:
                    return new CommandResult
                    {
                        Success = false,
                        Message = $"Geçersiz WiFi kontrolü komutu: {request.Action}",
                        Error = "Invalid WiFi action"
                    };
            }

            return new CommandResult
            {
                Success = success,
                Message = message
            };
        }
        catch (Exception ex)
        {
            return new CommandResult
            {
                Success = false,
                Message = $"WiFi kontrolü sırasında hata oluştu: {ex.Message}",
                Error = ex.Message
            };
        }
    }
}
