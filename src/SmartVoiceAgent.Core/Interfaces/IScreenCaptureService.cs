using SmartVoiceAgent.Core.Dtos;
using System.Drawing;

namespace SmartVoiceAgent.Core.Interfaces;
/// <summary>
/// Provides methods for capturing the screen.
/// </summary>
public interface IScreenCaptureService
{
    /// <summary>
    /// Captures all screens and returns a collection of frames.
    /// </summary>
    Task<IReadOnlyList<ScreenCaptureFrame>> CaptureAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures a specific screen by index.
    /// </summary>
    Task<ScreenCaptureFrame> CaptureScreenAsync(int screenIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures the primary screen.
    /// </summary>
    Task<ScreenCaptureFrame> CapturePrimaryScreenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures a specific region from a screen.
    /// </summary>
    Task<ScreenCaptureFrame> CaptureRegionAsync(int screenIndex, Rectangle region, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about all available screens.
    /// </summary>
    Task<IReadOnlyList<ScreenInfo>> GetScreenInfoAsync();
}
