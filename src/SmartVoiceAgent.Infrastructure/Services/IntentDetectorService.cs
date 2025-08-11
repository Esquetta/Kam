using Core.CrossCuttingConcerns.Logging.Serilog;
using Microsoft.Extensions.Configuration;
using SmartVoiceAgent.Core.Config;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Enums;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Keyword-based intent detection service with Todoist task management support.
/// </summary>
public class IntentDetectorService : IIntentDetectionService
{
    private readonly LoggerServiceBase _logger;
    private readonly IntentConfig _config;
    private readonly Dictionary<string, List<IntentPattern>> _intentPatterns;
    private readonly Dictionary<string, Regex> _entityRegexes;

    public IntentDetectorService(LoggerServiceBase logger, IConfiguration configuration)
    {
        _logger = logger;
        _config = configuration.GetSection("Intent").Get<IntentConfig>() ?? throw new NullReferenceException("Intent section cannot found in configuration.");
        _intentPatterns = LoadIntentPatterns();
        _entityRegexes = LoadEntityRegexes();
    }

    public IntentDetectorService()
    {
    }

    public async Task<IntentResult> DetectIntentAsync(string text, string language, CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken); // async context

        if (string.IsNullOrWhiteSpace(text))
        {
            return new IntentResult { Intent = CommandType.Unknown };
        }

        try
        {
            var normalizedText = NormalizeText(text);
            var patterns = GetPatternsForLanguage(language);

            var matches = patterns
                .Select(pattern => new
                {
                    Intent = pattern.Intent,
                    Pattern = pattern,
                    Score = CalculateScore(normalizedText, pattern)
                })
                .Where(x => x.Score >= _config.MinimumConfidence)
                .OrderByDescending(x => x.Score)
                .ToList();

            if (!matches.Any())
            {
                return new IntentResult
                {
                    Intent = CommandType.Unknown,
                    OriginalText = text,
                    Language = language
                };
            }

            var bestMatch = matches.First();
            var entities = await ExtractEntitiesAsync(text, bestMatch.Intent, language);

            return new IntentResult
            {
                Intent = bestMatch.Intent,
                Confidence = bestMatch.Score,
                Entities = entities,
                Language = language,
                OriginalText = text
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"Intent detection failed: {ex.Message}");
            return new IntentResult
            {
                Intent = CommandType.Unknown,
                OriginalText = text,
                Language = language
            };
        }
    }

    private Dictionary<string, List<IntentPattern>> LoadIntentPatterns()
    {
        return new Dictionary<string, List<IntentPattern>>
        {
            ["en"] = new List<IntentPattern>
            {
                new IntentPattern(CommandType.OpenApplication, new[] { "open", "start", "launch" }),
                new IntentPattern(CommandType.PlayMusic, new[] { "music", "song", "play" }),
                new IntentPattern(CommandType.SendMessage, new[] { "message", "send", "sms" }),
                new IntentPattern(CommandType.SearchWeb, new[] { "search", "google", "find" }),
                new IntentPattern(CommandType.CloseApplication, new[] { "close", "stop", "kill" }),

                // Todoist related intents
                new IntentPattern(CommandType.AddTask, new[] { "add", "create", "new", "task", "reminder" }),
                new IntentPattern(CommandType.UpdateTask, new[] { "update", "change", "edit", "modify" }),
                new IntentPattern(CommandType.DeleteTask, new[] { "delete", "remove", "cancel", "sil", "kaldır" }),
                new IntentPattern(CommandType.ListTasks, new[] { "list", "show", "display", "tasks", "görevler" }),
                new IntentPattern(CommandType.SetReminder, new[] { "remind", "reminder", "hatırlat", "alarm" }),
            },
            ["tr"] = new List<IntentPattern>
            {
                new IntentPattern(CommandType.OpenApplication, new[] { "aç", "başlat", "çalıştır" }),
                new IntentPattern(CommandType.PlayMusic, new[] { "müzik", "şarkı", "çal" }),
                new IntentPattern(CommandType.SendMessage, new[] { "mesaj", "gönder" }),
                new IntentPattern(CommandType.SearchWeb, new[] { "ara", "bul", "google" }),
                new IntentPattern(CommandType.CloseApplication, new[] { "kapat", "durdur", "sonlandır" }),

                // Todoist related intents
                new IntentPattern(CommandType.AddTask, new[] { "ekle", "oluştur", "yeni", "görev", "hatırlatıcı" }),
                new IntentPattern(CommandType.UpdateTask, new[] { "güncelle", "değiştir", "düzenle" }),
                new IntentPattern(CommandType.DeleteTask, new[] { "sil", "kaldır", "iptal" }),
                new IntentPattern(CommandType.ListTasks, new[] { "listele", "göster", "görevler" }),
                new IntentPattern(CommandType.SetReminder, new[] { "hatırlat", "alarm", "bildirim" }),
            }
        };
    }

    private Dictionary<string, Regex> LoadEntityRegexes()
    {
        return new Dictionary<string, Regex>
        {
            ["time"] = new Regex(@"\b\d{1,2}:\d{2}\b"), // 14:30 gibi saat
            ["date"] = new Regex(@"\b\d{1,2}/\d{1,2}/\d{4}\b"), // 15/08/2025 gibi tarih
            ["dateAlt"] = new Regex(@"\b\d{1,2}\.\d{1,2}\.\d{4}\b"), // 15.08.2025 gibi alternatif tarih
            ["number"] = new Regex(@"\b\d+\b"),
            ["priority"] = new Regex(@"\b(high|medium|low|yüksek|orta|düşük|önemli|acil)\b", RegexOptions.IgnoreCase),
            ["repeat"] = new Regex(@"\b(every day|daily|weekly|monthly|her gün|haftalık|aylık)\b", RegexOptions.IgnoreCase),
        };
    }

    private string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalizedText = Regex.Replace(text, @"[^\w\s\u00C0-\u017F]", " ");
        normalizedText = Regex.Replace(normalizedText, @"\s+", " ");

        // istersen Türkçe karakter normalizasyonu açılabilir
        // normalizedText = NormalizeTurkishChars(normalizedText);

        return normalizedText.ToLowerInvariant().Trim();
    }

    private string NormalizeTurkishChars(string text)
    {
        var turkishChars = new Dictionary<char, char>
        {
            {'ç', 'c'}, {'ğ', 'g'}, {'ı', 'i'}, {'ö', 'o'}, {'ş', 's'}, {'ü', 'u'},
            {'Ç', 'C'}, {'Ğ', 'G'}, {'İ', 'I'}, {'Ö', 'O'}, {'Ş', 'S'}, {'Ü', 'U'}
        };

        var sb = new StringBuilder();
        foreach (char c in text)
        {
            sb.Append(turkishChars.ContainsKey(c) ? turkishChars[c] : c);
        }
        return sb.ToString();
    }

    private List<IntentPattern> GetPatternsForLanguage(string language)
    {
        return _intentPatterns.ContainsKey(language)
            ? _intentPatterns[language]
            : _intentPatterns["en"];
    }

    private float CalculateScore(string text, IntentPattern pattern)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int matchCount = 0;

        foreach (var keyword in pattern.Keywords)
        {
            if (words.Any(word => IsLikeMatch(word, keyword)))
            {
                matchCount++;
            }
        }

        return (float)matchCount / pattern.Keywords.Length * pattern.Weight;
    }

    private bool IsLikeMatch(string word, string keyword)
    {
        word = StripTurkishSuffixes(word);

        return word.StartsWith(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private string StripTurkishSuffixes(string word)
    {
        return Regex.Replace(word, "(misin|mısın|mışsın|mişsin|sana|sene|abilir|ebilir|lütfen)$", "", RegexOptions.IgnoreCase);
    }

    private async Task<Dictionary<string, object>> ExtractEntitiesAsync(string text, CommandType intent, string language)
    {
        await Task.Delay(1);
        var entities = new Dictionary<string, object>();

        // Regex ile entity yakalama
        foreach (var kvp in _entityRegexes)
        {
            var matches = kvp.Value.Matches(text);
            if (matches.Any())
            {
                entities[kvp.Key] = matches.Select(m => m.Value).ToArray();
            }
        }

        // Görev adı çıkarımı: taskName
        if (intent == CommandType.AddTask || intent == CommandType.UpdateTask)
        {
            var patternKeywords = GetPatternsForLanguage(language)
                                  .FirstOrDefault(p => p.Intent == intent)?
                                  .Keywords ?? Array.Empty<string>();

            var normalizedText = NormalizeText(text);

            // TaskName çıkarımı için intent keywordlerini çıkar
            foreach (var keyword in patternKeywords)
            {
                normalizedText = Regex.Replace(normalizedText, $@"\b{Regex.Escape(keyword)}\b", "", RegexOptions.IgnoreCase);
            }

            normalizedText = normalizedText.Trim();

            if (!string.IsNullOrEmpty(normalizedText))
            {
                entities["taskName"] = normalizedText;
            }
        }

        return entities;
    }
}
