using Core.CrossCuttingConcerns.Logging.Serilog;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Enums;

namespace SmartVoiceAgent.Infrastructure.Services;

public class ContextAwareIntentDetectionService : IIntentDetectionService
{
    private readonly IIntentDetectionService _baseService;
    private readonly ConversationContextManager _contextManager;
    private readonly LoggerServiceBase _logger;

    public ContextAwareIntentDetectionService(
        IntentDetectorService baseService, // Your existing pattern-based service
        ConversationContextManager contextManager,
        LoggerServiceBase logger)
    {
        _baseService = baseService;
        _contextManager = contextManager;
        _logger = logger;
    }

    public async Task<IntentResult> DetectIntentAsync(string text, string language, CancellationToken cancellationToken = default)
    {
        // Get base intent detection
        var baseResult = await _baseService.DetectIntentAsync(text, language, cancellationToken);

        // Get conversation context using existing method
        var contextInfo = _contextManager.GetRelevantContext(text);

        // Apply context-aware adjustments
        var contextAwareResult = ApplyContextualAdjustments(baseResult, contextInfo, text);

        // Update context with new intent using existing method
        _contextManager.UpdateContext("IntentDetection", text, contextAwareResult.Intent.ToString());

        return contextAwareResult;
    }

    private IntentResult ApplyContextualAdjustments(IntentResult baseResult, string contextInfo, string text)
    {
        var textLower = text.ToLower();

        // Rule 1: If user says "aç" and there's a recent application mention in context
        if (textLower.Contains("aç") || textLower.Contains("başlat") || textLower.Contains("çalıştır"))
        {
            var appName = ExtractApplicationFromText(text);
            if (!string.IsNullOrEmpty(appName) || ContainsApplicationInContext(contextInfo))
            {
                return new IntentResult
                {
                    Intent = CommandType.OpenApplication,
                    Confidence = Math.Max(baseResult.Confidence, 0.9f),
                    Entities = CreateApplicationEntities(appName ?? ExtractApplicationFromContext(contextInfo)),
                    Language = baseResult.Language,
                    OriginalText = baseResult.OriginalText
                };
            }
        }

        // Rule 2: If discussing music and user says "çal", prioritize PlayMusic
        if ((textLower.Contains("çal") || textLower.Contains("oynat")) &&
            (ContainsMusicKeywords(contextInfo) || ContainsMusicKeywords(text)))
        {
            // But not if it's clearly an application opening command
            if (!textLower.Contains("spotify'i") && !textLower.Contains("uygulamayı"))
            {
                return new IntentResult
                {
                    Intent = CommandType.PlayMusic,
                    Confidence = Math.Max(baseResult.Confidence, 0.85f),
                    Entities = baseResult.Entities ?? new Dictionary<string, object>(),
                    Language = baseResult.Language,
                    OriginalText = baseResult.OriginalText
                };
            }
        }

        // Rule 3: If an application is already open and user says "kapat"
        if (textLower.Contains("kapat") || textLower.Contains("sonlandır"))
        {
            var openApps = ExtractOpenApplicationsFromContext(contextInfo);
            if (openApps.Any())
            {
                var appToClose = ExtractApplicationFromText(text) ?? openApps.First();
                return new IntentResult
                {
                    Intent = CommandType.CloseApplication,
                    Confidence = Math.Max(baseResult.Confidence, 0.9f),
                    Entities = CreateApplicationEntities(appToClose),
                    Language = baseResult.Language,
                    OriginalText = baseResult.OriginalText
                };
            }
        }

        // Rule 4: Context-based application preference
        if (baseResult.Intent == CommandType.PlayMusic && ContainsPreferredMusicApp(contextInfo))
        {
            var preferredApp = ExtractPreferredMusicApp(contextInfo);
            if (!string.IsNullOrEmpty(preferredApp))
            {
                var entities = baseResult.Entities ?? new Dictionary<string, object>();
                entities["preferredApplication"] = preferredApp;

                return new IntentResult
                {
                    Intent = baseResult.Intent,
                    Confidence = baseResult.Confidence + 0.1f, // Slight boost for context awareness
                    Entities = entities,
                    Language = baseResult.Language,
                    OriginalText = baseResult.OriginalText
                };
            }
        }

        return baseResult;
    }

    private string ExtractApplicationFromText(string text)
    {
        var appNames = new[] { "spotify", "chrome", "firefox", "notepad", "word", "excel", "vlc", "calculator" };
        var textLower = text.ToLower();

        foreach (var app in appNames)
        {
            if (textLower.Contains(app))
                return app;
        }

        return null;
    }

    private bool ContainsApplicationInContext(string contextInfo)
    {
        if (string.IsNullOrEmpty(contextInfo)) return false;

        var appNames = new[] { "spotify", "chrome", "firefox", "notepad", "word", "excel" };
        var contextLower = contextInfo.ToLower();

        return appNames.Any(app => contextLower.Contains(app));
    }

    private string ExtractApplicationFromContext(string contextInfo)
    {
        if (string.IsNullOrEmpty(contextInfo)) return null;

        var appNames = new[] { "spotify", "chrome", "firefox", "notepad", "word", "excel" };
        var contextLower = contextInfo.ToLower();

        return appNames.FirstOrDefault(app => contextLower.Contains(app));
    }

    private List<string> ExtractOpenApplicationsFromContext(string contextInfo)
    {
        var openApps = new List<string>();

        if (string.IsNullOrEmpty(contextInfo)) return openApps;

        // Parse "Açık uygulamalar: app1, app2, app3" format
        var openAppsSection = contextInfo.Split('|')
            .FirstOrDefault(section => section.Trim().StartsWith("Açık uygulamalar:") ||
                                     section.Trim().StartsWith("Ak uygulamalar:"));

        if (!string.IsNullOrEmpty(openAppsSection))
        {
            var appsText = openAppsSection.Split(':').LastOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(appsText))
            {
                openApps.AddRange(appsText.Split(',').Select(app => app.Trim()));
            }
        }

        return openApps;
    }

    private bool ContainsMusicKeywords(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        var musicKeywords = new[] { "müzik", "şarkı", "spotify", "music", "song", "çal", "oynat" };
        var textLower = text.ToLower();

        return musicKeywords.Any(keyword => textLower.Contains(keyword));
    }

    private bool ContainsPreferredMusicApp(string contextInfo)
    {
        if (string.IsNullOrEmpty(contextInfo)) return false;

        return contextInfo.ToLower().Contains("preferred_music_app:");
    }

    private string ExtractPreferredMusicApp(string contextInfo)
    {
        if (string.IsNullOrEmpty(contextInfo)) return null;

        // Parse "Kullanıcı tercihleri: preferred_music_app: spotify" format
        var preferencesSection = contextInfo.Split('|')
            .FirstOrDefault(section => section.Trim().StartsWith("Kullanıcı tercihleri:") ||
                                     section.Trim().StartsWith("Kullanc tercihleri:"));

        if (!string.IsNullOrEmpty(preferencesSection))
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                preferencesSection,
                @"preferred_music_app:\s*(\w+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    private Dictionary<string, object> CreateApplicationEntities(string appName)
    {
        var entities = new Dictionary<string, object>();

        if (!string.IsNullOrEmpty(appName))
        {
            entities["applicationName"] = appName;
            entities["action"] = "open"; // Default action
        }

        return entities;
    }
}
