using SmartVoiceAgent.Core.EventArgs;

namespace SmartVoiceAgent.Core.Interfaces;

public interface IVoicePipelineService
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    event EventHandler<VoiceProcessedEventArgs> OnVoiceProcessed;
    event EventHandler<PipelineErrorEventArgs> OnError;
}