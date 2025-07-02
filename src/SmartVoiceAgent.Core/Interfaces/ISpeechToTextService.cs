using SmartVoiceAgent.Core.Entities;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ISpeechToTextService
{
    Task<SpeechResult> ConvertToTextAsync(byte[] audioData, CancellationToken cancellationToken = default);
}

