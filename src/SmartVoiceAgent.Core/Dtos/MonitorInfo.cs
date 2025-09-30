using System.Drawing;

namespace SmartVoiceAgent.Core.Dtos;
public class MonitorInfo
{
    public IntPtr Handle { get; set; }
    public Rectangle Bounds { get; set; }
    public Rectangle WorkingArea { get; set; }
    public bool IsPrimary { get; set; }
    public string DeviceName { get; set; }
}
