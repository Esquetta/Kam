using System.Drawing;

namespace SmartVoiceAgent.Core.Interfaces;
/// <summary>
/// Provides methods for capturing the screen.
/// </summary>
public interface IScreenCaptureService
{
    /// <summary>
    /// Captures the entire screen as a bitmap.
    /// </summary>
    /// <returns>Bitmap image of the current screen.</returns>
    Task<Bitmap> CaptureScreenAsync();
}
