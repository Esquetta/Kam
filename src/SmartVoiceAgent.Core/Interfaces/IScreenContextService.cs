using SmartVoiceAgent.Core.Dtos;

namespace SmartVoiceAgent.Core.Interfaces;
public interface IScreenContextService
{
    /// <summary>
    /// Captures the screen, runs OCR, and builds a full screen context.
    /// </summary>
    Task<ScreenContext> CaptureAndAnalyzeAsync();
}
