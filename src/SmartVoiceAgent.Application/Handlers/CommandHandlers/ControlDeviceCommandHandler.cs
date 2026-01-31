using MediatR;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Application.Notifications;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers.CommandHandlers;

/// <summary>
/// Handles the ControlDeviceCommand by performing the specified action on a device.
/// Supports: volume, wifi, bluetooth, screen (brightness), power
/// </summary>
public class ControlDeviceCommandHandler : IRequestHandler<ControlDeviceCommand, CommandResultDTO>
{
    private readonly IMediator _mediator;
    private readonly ISystemControlServiceFactory _systemControlFactory;
    private readonly ILogger<ControlDeviceCommandHandler>? _logger;

    public ControlDeviceCommandHandler(
        IMediator mediator,
        ISystemControlServiceFactory systemControlFactory,
        ILogger<ControlDeviceCommandHandler>? logger = null)
    {
        _mediator = mediator;
        _systemControlFactory = systemControlFactory;
        _logger = logger;
    }

    public async Task<CommandResultDTO> Handle(ControlDeviceCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger?.LogInformation("🎛️ Device control request: {Action} on {Device}", 
                request.Action, request.DeviceName);

            var systemControl = _systemControlFactory.CreateSystemService();
            var deviceName = request.DeviceName.ToLowerInvariant();
            var action = request.Action.ToLowerInvariant();
            
            bool success;
            string message;

            switch (deviceName)
            {
                case "volume":
                case "ses":
                    (success, message) = await HandleVolumeControlAsync(systemControl, action, cancellationToken);
                    break;

                case "brightness":
                case "screen":
                case "parlaklık":
                case "ekran":
                    (success, message) = await HandleBrightnessControlAsync(systemControl, action, cancellationToken);
                    break;

                case "wifi":
                case "wi-fi":
                case "internet":
                    (success, message) = await HandleWiFiControlAsync(systemControl, action, cancellationToken);
                    break;

                case "bluetooth":
                    (success, message) = await HandleBluetoothControlAsync(systemControl, action, cancellationToken);
                    break;

                case "power":
                case "system":
                    (success, message) = await HandlePowerControlAsync(systemControl, action, cancellationToken);
                    break;

                default:
                    _logger?.LogWarning("⚠️ Unknown device: {Device}", request.DeviceName);
                    return new CommandResultDTO(
                        false, 
                        $"Unknown device: {request.DeviceName}. Supported devices: volume, brightness, wifi, bluetooth, power.");
            }

            // Publish notification
            await _mediator.Publish(
                new DeviceControlledNotification(request.DeviceName, request.Action) 
                { 
                    Success = success,
                    Message = message 
                }, 
                cancellationToken);

            _logger?.LogInformation("✅ Device control result: {Success} - {Message}", success, message);

            return new CommandResultDTO(success, message);
        }
        catch (PlatformNotSupportedException ex)
        {
            _logger?.LogError(ex, "❌ Platform not supported for device control");
            return new CommandResultDTO(false, 
                "Device control is not supported on this operating system.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ Error controlling device: {Device} with action: {Action}", 
                request.DeviceName, request.Action);
            return new CommandResultDTO(false, 
                $"Failed to {request.Action} {request.DeviceName}: {ex.Message}");
        }
    }

    private async Task<(bool success, string message)> HandleVolumeControlAsync(
        ISystemControlService systemControl, 
        string action, 
        CancellationToken cancellationToken)
    {
        bool success;
        int currentLevel;

        switch (action)
        {
            case "increase":
            case "up":
            case "yukarı":
            case "artır":
                success = await systemControl.IncreaseSystemVolumeAsync(10);
                currentLevel = await systemControl.GetSystemVolumeAsync();
                return (success, $"Volume increased to {currentLevel}%");

            case "decrease":
            case "down":
            case "aşağı":
            case "azalt":
                success = await systemControl.DecreaseSystemVolumeAsync(10);
                currentLevel = await systemControl.GetSystemVolumeAsync();
                return (success, $"Volume decreased to {currentLevel}%");

            case "mute":
            case "sessiz":
                success = await systemControl.MuteSystemVolumeAsync();
                return (success, "Volume muted");

            case "unmute":
            case "sesli":
                success = await systemControl.UnmuteSystemVolumeAsync();
                return (success, "Volume unmuted");

            case "set":
                // For set, we'd need a value parameter - handled via specific command
                return (false, "Use 'set volume to X' format with a specific level");

            default:
                return (false, $"Unknown volume action: {action}. Try: increase, decrease, mute, unmute");
        }
    }

    private async Task<(bool success, string message)> HandleBrightnessControlAsync(
        ISystemControlService systemControl, 
        string action, 
        CancellationToken cancellationToken)
    {
        bool success;
        int currentLevel;

        switch (action)
        {
            case "increase":
            case "up":
            case "artır":
                success = await systemControl.IncreaseScreenBrightnessAsync(10);
                currentLevel = await systemControl.GetScreenBrightnessAsync();
                return (success, $"Screen brightness increased to {currentLevel}%");

            case "decrease":
            case "down":
            case "azalt":
                success = await systemControl.DecreaseScreenBrightnessAsync(10);
                currentLevel = await systemControl.GetScreenBrightnessAsync();
                return (success, $"Screen brightness decreased to {currentLevel}%");

            case "set":
                return (false, "Use 'set brightness to X' format with a specific level");

            default:
                return (false, $"Unknown brightness action: {action}. Try: increase, decrease");
        }
    }

    private async Task<(bool success, string message)> HandleWiFiControlAsync(
        ISystemControlService systemControl, 
        string action, 
        CancellationToken cancellationToken)
    {
        bool success;

        switch (action)
        {
            case "on":
            case "enable":
            case "aç":
                success = await systemControl.EnableWiFiAsync();
                return (success, success ? "WiFi enabled" : "Failed to enable WiFi");

            case "off":
            case "disable":
            case "kapat":
                success = await systemControl.DisableWiFiAsync();
                return (success, success ? "WiFi disabled" : "Failed to disable WiFi");

            case "status":
            case "durum":
                var isEnabled = await systemControl.GetWiFiStatusAsync();
                return (true, $"WiFi is {(isEnabled ? "enabled" : "disabled")}");

            default:
                return (false, $"Unknown WiFi action: {action}. Try: on, off, status");
        }
    }

    private async Task<(bool success, string message)> HandleBluetoothControlAsync(
        ISystemControlService systemControl, 
        string action, 
        CancellationToken cancellationToken)
    {
        bool success;

        switch (action)
        {
            case "on":
            case "enable":
            case "aç":
                success = await systemControl.EnableBluetoothAsync();
                return (success, success ? "Bluetooth enabled" : "Failed to enable Bluetooth");

            case "off":
            case "disable":
            case "kapat":
                success = await systemControl.DisableBluetoothAsync();
                return (success, success ? "Bluetooth disabled" : "Failed to disable Bluetooth");

            case "status":
            case "durum":
                var isEnabled = await systemControl.GetBluetoothStatusAsync();
                return (true, $"Bluetooth is {(isEnabled ? "enabled" : "disabled")}");

            default:
                return (false, $"Unknown Bluetooth action: {action}. Try: on, off, status");
        }
    }

    private async Task<(bool success, string message)> HandlePowerControlAsync(
        ISystemControlService systemControl, 
        string action, 
        CancellationToken cancellationToken)
    {
        bool success;

        switch (action)
        {
            case "shutdown":
            case "kapat":
                success = await systemControl.ShutdownSystemAsync(0);
                return (success, success ? "System shutting down" : "Failed to shutdown system");

            case "restart":
            case "reboot":
            case "yeniden başlat":
                success = await systemControl.RestartSystemAsync(0);
                return (success, success ? "System restarting" : "Failed to restart system");

            case "sleep":
            case "suspend":
            case "uyku":
                success = await systemControl.SleepSystemAsync();
                return (success, success ? "System going to sleep" : "Failed to sleep system");

            case "lock":
            case "kilitle":
                success = await systemControl.LockSystemAsync();
                return (success, success ? "System locked" : "Failed to lock system");

            case "status":
            case "durum":
                var status = await systemControl.GetSystemStatusAsync();
                return (true, 
                    $"System Status - Volume: {status.VolumeLevel}%, " +
                    $"Brightness: {status.BrightnessLevel}%, " +
                    $"WiFi: {(status.IsWiFiEnabled ? "On" : "Off")}, " +
                    $"Bluetooth: {(status.IsBluetoothEnabled ? "On" : "Off")}, " +
                    $"Battery: {status.BatteryLevel}%");

            default:
                return (false, $"Unknown power action: {action}. Try: shutdown, restart, sleep, lock, status");
        }
    }
}
