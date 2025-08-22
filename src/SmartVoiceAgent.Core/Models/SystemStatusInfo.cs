namespace SmartVoiceAgent.Core.Models;


public class SystemStatusInfo
{
    public int VolumeLevel { get; set; }
    public bool IsMuted { get; set; }
    public int BrightnessLevel { get; set; }
    public bool IsWiFiEnabled { get; set; }
    public string WiFiStatus { get; set; } = string.Empty;
    public bool IsBluetoothEnabled { get; set; }
    public string BluetoothStatus { get; set; } = string.Empty;
    public int BatteryLevel { get; set; }
    public bool IsCharging { get; set; }
    public string PowerStatus { get; set; } = string.Empty;
    public double CpuUsage { get; set; }
    public long MemoryUsage { get; set; }
    public long TotalMemory { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.Now;
}
