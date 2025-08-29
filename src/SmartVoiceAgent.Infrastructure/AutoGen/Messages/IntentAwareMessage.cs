using AutoGen.Core;
using SmartVoiceAgent.Core.Enums;

namespace SmartVoiceAgent.Infrastructure.AutoGen.Messages;
// <summary>
/// Intent detection sonuçlarını içeren gelişmiş mesaj tipi
/// </summary>
public class IntentAwareMessage : IMessage
{
    public string Content { get; set; }
    public string From { get; set; }
    public Role Role { get; set; }

    // Intent detection sonuçları
    public CommandType DetectedIntent { get; set; }
    public float Confidence { get; set; }
    public Dictionary<string, object> Entities { get; set; }
    public DateTime IntentDetectedAt { get; set; }

    public IntentAwareMessage(
        string content,
        string from,
        Role role,
        CommandType detectedIntent,
        float confidence,
        Dictionary<string, object> entities)
    {
        Content = content;
        From = from;
        Role = role;
        DetectedIntent = detectedIntent;
        Confidence = confidence;
        Entities = entities ?? new Dictionary<string, object>();
        IntentDetectedAt = DateTime.UtcNow;
    }

    public string GetContent() => Content;
    public Role GetRole() => Role;

    /// <summary>
    /// Intent bilgisini hızlıca kontrol etmek için yardımcı metod
    /// </summary>
    public bool HasIntent(CommandType intent, float minConfidence = 0.3f)
    {
        return DetectedIntent == intent && Confidence >= minConfidence;
    }

    /// <summary>
    /// Birden fazla intent'i kontrol etmek için
    /// </summary>
    public bool HasAnyIntent(IEnumerable<CommandType> intents, float minConfidence = 0.3f)
    {
        return intents.Contains(DetectedIntent) && Confidence >= minConfidence;
    }
}