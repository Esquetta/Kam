using AutoGen.Core;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Infrastructure.AutoGen.Messages;

namespace SmartVoiceAgent.Infrastructure.AutoGen.Middlewares;
/// <summary>
/// Mesajları intent-aware mesajlara dönüştüren middleware
/// Sadece User mesajları için intent detection yapıp sonucu pipeline'a ekler
/// </summary>
public class IntentDetectionMiddleware : IMiddleware
{
    private readonly IIntentDetectionService _intentDetectionService;
    private static readonly Dictionary<string, (CommandType intent, float confidence, Dictionary<string, object> entities, DateTime timestamp)>
        _intentCache = new();
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);

    public IntentDetectionMiddleware(IIntentDetectionService intentDetectionService)
    {
        _intentDetectionService = intentDetectionService;
    }

    public string? Name => "IntentDetectionMiddleware";

    public async Task<IMessage> InvokeAsync(
        MiddlewareContext context,
        IAgent agent,
        CancellationToken cancellationToken = default)
    {
        // Intent detection sadece User mesajları için ve Coordinator agent'ta yapılır
        if (agent.Name == "Coordinator")
        {
            var enrichedMessages = new List<IMessage>();

            foreach (var message in context.Messages)
            {
                if (message.From == "User" && !(message is IntentAwareMessage))
                {
                    var intentAwareMessage = await EnrichWithIntent(message);
                    enrichedMessages.Add(intentAwareMessage);

                    Console.WriteLine($"🧠 Intent detected: {intentAwareMessage.DetectedIntent} " +
                                    $"(Confidence: {intentAwareMessage.Confidence:F2})");
                }
                else
                {
                    enrichedMessages.Add(message);
                }
            }

            // Enriched context ile devam et
            var enrichedContext = new MiddlewareContext(enrichedMessages, context.Options);
            return await agent.GenerateReplyAsync(enrichedContext.Messages, enrichedContext.Options, cancellationToken);
        }

        // Diğer agent'lar için normal processing
        return await agent.GenerateReplyAsync(context.Messages, context.Options, cancellationToken);
    }

    private async Task<IntentAwareMessage> EnrichWithIntent(IMessage originalMessage)
    {
        var content = originalMessage.GetContent() ?? "";
        var cacheKey = content.Trim().ToLower();

        // Cache kontrolü
        if (_intentCache.TryGetValue(cacheKey, out var cached))
        {
            if (DateTime.UtcNow - cached.timestamp < CacheExpiry)
            {
                Console.WriteLine($"💾 Intent cache hit for: {content.Substring(0, Math.Min(50, content.Length))}...");

                return new IntentAwareMessage(
                    content,
                    originalMessage.From,
                    originalMessage.GetRole().Value,
                    cached.intent,
                    cached.confidence,
                    cached.entities);
            }
            else
            {
                // Cache expired
                _intentCache.Remove(cacheKey);
            }
        }

        try
        {
            Console.WriteLine($"🔍 Detecting intent for: {content.Substring(0, Math.Min(50, content.Length))}...");

            var intentResult = await _intentDetectionService.DetectIntentAsync(content, "tr");

            // Cache'e ekle
            _intentCache[cacheKey] = (intentResult.Intent, intentResult.Confidence,
                                    intentResult.Entities ?? new Dictionary<string, object>(),
                                    DateTime.UtcNow);

            return new IntentAwareMessage(
                content,
                originalMessage.From,
                originalMessage.GetRole().Value,
                intentResult.Intent,
                intentResult.Confidence,
                intentResult.Entities ?? new Dictionary<string, object>());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Intent detection failed: {ex.Message}");

            // Fallback: Default intent
            return new IntentAwareMessage(
                content,
                originalMessage.From,
                originalMessage.GetRole().Value,
                CommandType.Unknown,
                0.0f,
                new Dictionary<string, object>());
        }
    }

    /// <summary>
    /// Cache temizleme metodu
    /// </summary>
    public static void CleanExpiredCache()
    {
        var now = DateTime.UtcNow;
        var expiredKeys = _intentCache
            .Where(kvp => now - kvp.Value.timestamp > CacheExpiry)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _intentCache.Remove(key);
        }

        if (expiredKeys.Any())
        {
            Console.WriteLine($"🧹 Cleaned {expiredKeys.Count} expired intent cache entries");
        }
    }
}