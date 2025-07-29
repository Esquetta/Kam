using SmartVoiceAgent.Core.Models;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ISpeechToTextService: IDisposable
{
    Task<SpeechResult> ConvertToTextAsync(byte[] audioData, CancellationToken cancellationToken = default);
}

