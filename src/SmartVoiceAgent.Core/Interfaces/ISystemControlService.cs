using SmartVoiceAgent.Core.Models;

namespace SmartVoiceAgent.Core.Interfaces;
public interface ISystemControlService
{
    Task<bool> SetSystemVolumeAsync(int level);
    Task<bool> IncreaseSystemVolumeAsync(int step = 10);
    Task<bool> DecreaseSystemVolumeAsync(int step = 10);
    Task<bool> MuteSystemVolumeAsync();
    Task<bool> UnmuteSystemVolumeAsync();
    Task<int> GetSystemVolumeAsync();

    Task<bool> SetScreenBrightnessAsync(int level);
    Task<bool> IncreaseScreenBrightnessAsync(int step = 10);
    Task<bool> DecreaseScreenBrightnessAsync(int step = 10);
    Task<int> GetScreenBrightnessAsync();

    Task<bool> EnableWiFiAsync();
    Task<bool> DisableWiFiAsync();
    Task<bool> GetWiFiStatusAsync();

    Task<bool> EnableBluetoothAsync();
    Task<bool> DisableBluetoothAsync();
    Task<bool> GetBluetoothStatusAsync();

    Task<bool> ShutdownSystemAsync(int delayMinutes = 0);
    Task<bool> RestartSystemAsync(int delayMinutes = 0);
    Task<bool> SleepSystemAsync();
    Task<bool> LockSystemAsync();

    Task<SystemStatusInfo> GetSystemStatusAsync();
}
