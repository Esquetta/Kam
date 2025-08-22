using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Interfaces;
using System.Text.Json;

namespace SmartVoiceAgent.Application.Handlers.CommandHandlers;
public sealed class GetSystemStatusCommandHandler : IRequestHandler<GetSystemStatusCommand, CommandResult>
{
    private readonly ISystemControlServiceFactory _systemControlFactory;

    public GetSystemStatusCommandHandler(ISystemControlServiceFactory systemControlFactory)
    {
        _systemControlFactory = systemControlFactory;
    }

    public async Task<CommandResult> Handle(GetSystemStatusCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var systemControl = _systemControlFactory.CreateSystemService();
            var infoType = request.InfoType.ToLower();
            string message = "";
            object data = null;

            switch (infoType)
            {
                case "volume":
                case "ses":
                    var volume = await systemControl.GetSystemVolumeAsync();
                    message = $"Ses seviyesi: %{volume}";
                    data = new { VolumeLevel = volume };
                    break;

                case "brightness":
                case "parlaklık":
                    var brightness = await systemControl.GetScreenBrightnessAsync();
                    message = $"Ekran parlaklığı: %{brightness}";
                    data = new { BrightnessLevel = brightness };
                    break;

                case "wifi":
                case "internet":
                    var wifiStatus = await systemControl.GetWiFiStatusAsync();
                    message = $"WiFi durumu: {(wifiStatus ? "Açık" : "Kapalı")}";
                    data = new { WiFiEnabled = wifiStatus };
                    break;

                case "bluetooth":
                case "mavi diş":
                    var bluetoothStatus = await systemControl.GetBluetoothStatusAsync();
                    message = $"Bluetooth durumu: {(bluetoothStatus ? "Açık" : "Kapalı")}";
                    data = new { BluetoothEnabled = bluetoothStatus };
                    break;

                case "battery":
                case "batarya":
                    var systemStatus = await systemControl.GetSystemStatusAsync();
                    message = $"Batarya seviyesi: %{systemStatus.BatteryLevel} ({(systemStatus.IsCharging ? "Şarj oluyor" : "Şarj olmuyor")})";
                    data = new
                    {
                        BatteryLevel = systemStatus.BatteryLevel,
                        IsCharging = systemStatus.IsCharging
                    };
                    break;

                case "all":
                case "tümü":
                case "hepsi":
                default:
                    var fullStatus = await systemControl.GetSystemStatusAsync();
                    message = $"Sistem Durumu:\n" +
                             $"• Ses: %{fullStatus.VolumeLevel}\n" +
                             $"• Ekran: %{fullStatus.BrightnessLevel}\n" +
                             $"• WiFi: {(fullStatus.IsWiFiEnabled ? "Açık" : "Kapalı")}\n" +
                             $"• Bluetooth: {(fullStatus.IsBluetoothEnabled ? "Açık" : "Kapalı")}\n" +
                             $"• Batarya: %{fullStatus.BatteryLevel} ({(fullStatus.IsCharging ? "Şarj oluyor" : "Şarj olmuyor")})\n" +
                             $"• CPU: %{fullStatus.CpuUsage:F1}\n" +
                             $"• Bellek: {fullStatus.MemoryUsage / (1024 * 1024 * 1024):F1}GB / {fullStatus.TotalMemory / (1024 * 1024 * 1024):F1}GB";
                    data = fullStatus;
                    break;
            }

            return new CommandResult
            {
                Success = true,
                Message = message,
                Data = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                })
            };
        }
        catch (Exception ex)
        {
            return new CommandResult
            {
                Success = false,
                Message = $"Sistem durumu alınırken hata oluştu: {ex.Message}",
                Error = ex.Message
            };
        }
    }
}