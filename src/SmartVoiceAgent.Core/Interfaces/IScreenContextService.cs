using SmartVoiceAgent.Core.Dtos.Screen;

namespace SmartVoiceAgent.Core.Interfaces;
public interface IScreenContextService
{
    /// <summary>
    /// Captures the screen, runs OCR, and builds a full screen context.
    /// </summary>
    Task<List<ScreenContext>> CaptureAndAnalyzeAsync(CancellationToken cancellationToken);
}
