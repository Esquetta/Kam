using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Interfaces;
using System.Text.Json;

namespace SmartVoiceAgent.Application.Handlers.CommandHandlers;

public sealed class GetSystemStatusCommandHandler : ICommandHandler<GetSystemStatusCommand, CommandResult>
{
    private readonly ISystemControlServiceFactory _systemControlFactory;

    // Performance: Reuse JsonSerializerOptions instance instead of creating per request
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public GetSystemStatusCommandHandler(ISystemControlServiceFactory systemControlFactory)
    {
        _systemControlFactory = systemControlFactory;
    }

    public async Task<CommandResult> Handle(GetSystemStatusCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var systemControl = _systemControlFactory.CreateSystemService();
            // Performance: Use ReadOnlySpan<char> for comparison to avoid string allocation
            var infoType = request.InfoType.AsSpan();
            string message;
            object data;

            // Performance: Use Span-based switch for zero-allocation comparison
            if (infoType.Equals("volume".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                infoType.Equals("ses".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                var volume = await systemControl.GetSystemVolumeAsync();
                message = $"Ses seviyesi: %{volume}";
                data = new { VolumeLevel = volume };
            }
            else if (infoType.Equals("brightness".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                     infoType.Equals("parlaklık".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                var brightness = await systemControl.GetScreenBrightnessAsync();
                message = $"Ekran parlaklığı: %{brightness}";
                data = new { BrightnessLevel = brightness };
            }
            else if (infoType.Equals("wifi".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                     infoType.Equals("internet".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                var wifiStatus = await systemControl.GetWiFiStatusAsync();
                message = $"WiFi durumu: {(wifiStatus ? "Açık" : "Kapalı")}";
                data = new { WiFiEnabled = wifiStatus };
            }
            else if (infoType.Equals("bluetooth".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                     infoType.Equals("mavi diş".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                var bluetoothStatus = await systemControl.GetBluetoothStatusAsync();
                message = $"Bluetooth durumu: {(bluetoothStatus ? "Açık" : "Kapalı")}";
                data = new { BluetoothEnabled = bluetoothStatus };
            }
            else if (infoType.Equals("battery".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                     infoType.Equals("batarya".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                var systemStatus = await systemControl.GetSystemStatusAsync();
                message = $"Batarya seviyesi: %{systemStatus.BatteryLevel} ({(systemStatus.IsCharging ? "Şarj oluyor" : "Şarj olmuyor")})";
                data = new
                {
                    systemStatus.BatteryLevel,
                    systemStatus.IsCharging
                };
            }
            else
            {
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
            }

            return new CommandResult
            {
                Success = true,
                Message = message,
                // Performance: Use cached JsonSerializerOptions
                Data = JsonSerializer.Serialize(data, s_jsonOptions)
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
