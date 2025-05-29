using SmartVoiceAgent.Core.Enums;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Basic intent detector service based on keyword matching.
/// </summary>
public class IntentDetectorService : IIntentDetector
{
    public Task<CommandType> DetectIntentAsync(string voiceInput)
    {
        if (string.IsNullOrWhiteSpace(voiceInput))
            return Task.FromResult(CommandType.ControlDevice);

        voiceInput = voiceInput.ToLower();

        if (voiceInput.Contains("aç") || voiceInput.Contains("başlat"))
            return Task.FromResult(CommandType.OpenApplication);
        if (voiceInput.Contains("mesaj") || voiceInput.Contains("gönder"))
            return Task.FromResult(CommandType.SendMessage);
        if (voiceInput.Contains("müzik") || voiceInput.Contains("çal"))
            return Task.FromResult(CommandType.PlayMusic);
        if (voiceInput.Contains("ara") || voiceInput.Contains("google"))
            return Task.FromResult(CommandType.SearchWeb);
        if (voiceInput.Contains("kapat") || voiceInput.Contains("durdur"))
            return Task.FromResult(CommandType.ControlDevice);

        return Task.FromResult(CommandType.ControlDevice);
    }
}
