using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers.CommandHandlers;
public sealed class ControlBluetoothCommandHandler : IRequestHandler<ControlBluetoothCommand, CommandResult>
{
    private readonly ISystemControlServiceFactory _systemControlFactory;

    public ControlBluetoothCommandHandler(ISystemControlServiceFactory systemControlFactory)
    {
        _systemControlFactory = systemControlFactory;
    }

    public async Task<CommandResult> Handle(ControlBluetoothCommand request, CancellationToken cancellationToken)
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
                    success = await systemControl.EnableBluetoothAsync();
                    message = success ? "Bluetooth açıldı." : "Bluetooth açılamadı.";
                    break;

                case "disable":
                case "kapat":
                case "kapalı":
                case "devre dışı":
                    success = await systemControl.DisableBluetoothAsync();
                    message = success ? "Bluetooth kapatıldı." : "Bluetooth kapatılamadı.";
                    break;

                case "toggle":
                case "değiştir":
                    var currentStatus = await systemControl.GetBluetoothStatusAsync();
                    if (currentStatus)
                    {
                        success = await systemControl.DisableBluetoothAsync();
                        message = success ? "Bluetooth kapatıldı." : "Bluetooth kapatılamadı.";
                    }
                    else
                    {
                        success = await systemControl.EnableBluetoothAsync();
                        message = success ? "Bluetooth açıldı." : "Bluetooth açılamadı.";
                    }
                    break;

                case "status":
                case "durum":
                    var status = await systemControl.GetBluetoothStatusAsync();
                    message = $"Bluetooth durumu: {(status ? "Açık" : "Kapalı")}";
                    success = true;
                    break;

                default:
                    return new CommandResult
                    {
                        Success = false,
                        Message = $"Geçersiz Bluetooth kontrolü komutu: {request.Action}",
                        Error = "Invalid Bluetooth action"
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
                Message = $"Bluetooth kontrolü sırasında hata oluştu: {ex.Message}",
                Error = ex.Message
            };
        }
    }
}
