using SmartVoiceAgent.Core.Models;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ISpeechToTextService
{
    Task<SpeechResult> ConvertToTextAsync(byte[] audioData, CancellationToken cancellationToken = default);
}

