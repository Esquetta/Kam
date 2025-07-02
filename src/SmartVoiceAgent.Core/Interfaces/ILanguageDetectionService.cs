using SmartVoiceAgent.Core.Entities;

namespace SmartVoiceAgent.Core.Interfaces;

public interface ILanguageDetectionService
{
    Task<LanguageResult> DetectLanguageAsync(string text, CancellationToken cancellationToken = default);
}
