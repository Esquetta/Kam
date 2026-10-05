using SmartVoiceAgent.Core.Dtos.Screen;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Screen context for platforms without screen capture support. Returns no screens,
/// so skills that only optionally use screen context still resolve and run.
/// </summary>
public sealed class UnsupportedScreenContextService : IScreenContextService
{
    /// <inheritdoc />
    public Task<List<ScreenContext>> CaptureAndAnalyzeAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new List<ScreenContext>());
    }
}
